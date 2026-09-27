using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Access.SignIn;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using SignInResult = Sethika.Application.Access.SignIn.SignInResult;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Sign-in against the real database, with the handler and its hasher resolved from the real host.
/// </summary>
/// <remarks>
/// Lockout, the failure count and the history rows are all database state, and the lockout's
/// resistance to concurrency is a property of one SQL UPDATE - none of which a fake would exercise.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class SignInHandlerTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "sign-in-test-signing-key-at-least-32-bytes-long!!";
    private const string Password = "Dana!2026Sethika";
    private const string WrongPassword = "Dana!2026Sethikx";

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public async Task DisposeAsync()
    {
        if (_users.Count > 0)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.ExecuteAsync(
                """
                DELETE FROM auth.RefreshTokens WHERE UserId IN @ids;
                DELETE FROM auth.LoginHistory  WHERE UserId IN @ids;
                DELETE FROM auth.UserRoles     WHERE UserId IN @ids;
                DELETE FROM auth.Users         WHERE Id     IN @ids;
                """,
                new { ids = _users });
        }

        await _host.DisposeAsync();
    }

    private IPasswordHasher<User> Hasher => _host.Services.GetRequiredService<IPasswordHasher<User>>();

    private async Task<SignInResult> SignInAsync(string? login, string? password)
    {
        // A scope per attempt, as each HTTP request would have: its own DbContext and connection.
        using var scope = _host.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SignInHandler>();

        return await handler.HandleAsync(new SignInCommand(login, password, "203.0.113.7", "xunit"));
    }

    private sealed class TestUser
    {
        public Guid Id { get; init; }
        public string Phone { get; init; } = null!;

        /// <summary>The same number as a Sri Lankan would type it: 07x xxx xxxx.</summary>
        public string LocalPhone => "0" + Phone[3..];
    }

    private async Task<TestUser> CreateUserAsync(
        string? email = null,
        string status = "Active",
        string userType = "Donor",
        string? passwordHash = null,
        bool withPassword = true)
    {
        var id = Guid.NewGuid();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        var hash = withPassword ? passwordHash ?? Hasher.HashPassword(new User(), Password) : null;

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, Email, NormalizedEmail, PhoneNumber, PhoneConfirmed,
                               PasswordHash, PreferredLanguage, Status)
            VALUES (@id, @userType, N'Sign-in Test', @email, @normalizedEmail, @phone, 1,
                    @hash, 'en', @status);
            """,
            new { id, userType, email, normalizedEmail = email?.ToUpperInvariant(), phone, hash, status });

        _users.Add(id);
        return new TestUser { Id = id, Phone = phone };
    }

    private sealed class UserState
    {
        public int AccessFailedCount { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTime? LastLoginAt { get; init; }
        public string? PasswordHash { get; init; }
    }

    private async Task<UserState> ReadUserAsync(Guid id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<UserState>(
            """
            SELECT AccessFailedCount, LockoutEnd, LastLoginAt, PasswordHash
            FROM auth.Users WHERE Id = @id;
            """,
            new { id });
    }

    private sealed class AttemptRow
    {
        public Guid? UserId { get; init; }
        public string LoginName { get; init; } = null!;
        public bool Succeeded { get; init; }
        public string? FailureReason { get; init; }
    }

    private async Task<List<AttemptRow>> ReadAttemptsAsync(string loginName)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<AttemptRow>(
            """
            SELECT UserId, LoginName, Succeeded, FailureReason
            FROM auth.LoginHistory WHERE LoginName = @loginName ORDER BY Id;
            """,
            new { loginName })];
    }

    private async Task SetLockoutEndAsync(Guid id, TimeSpan fromNow)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE auth.Users SET LockoutEnd = DATEADD(SECOND, @seconds, SYSDATETIMEOFFSET()) WHERE Id = @id;",
            new { id, seconds = (int)fromNow.TotalSeconds });
    }

    private async Task AssignRoleAsync(Guid userId, int institutionId, DateTime? validTo = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DECLARE @roleId INT = (SELECT Id FROM auth.Roles WHERE InstitutionId = @institutionId AND Code = 'SIGNIN_TEST');
            IF @roleId IS NULL
            BEGIN
                INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
                VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'),
                        'SIGNIN_TEST', N'Sign-in test', 0);
                SET @roleId = CAST(SCOPE_IDENTITY() AS INT);
            END;

            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive, ValidTo)
            VALUES (@userId, @roleId, @institutionId, 1, @validTo);
            """,
            new { userId, institutionId, validTo });
    }

    private static int? InstitutionClaim(SignInResult result)
    {
        var jwt = new JsonWebToken(result.Tokens!.Access.Token);

        return jwt.TryGetPayloadValue<int>(SethikaClaimTypes.InstitutionId, out var institutionId)
            ? institutionId
            : null;
    }

    [Fact]
    public async Task SignsInWithTheLocalSpellingOfAStoredE164Number_AndStoresTheRefreshTokenHash()
    {
        var user = await CreateUserAsync();

        var result = await SignInAsync(user.LocalPhone, Password);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Tokens);

        await using var connection = new SqlConnection(database.ConnectionString);
        var stored = await connection.QuerySingleAsync<(string TokenHash, string JwtId, Guid FamilyId, DateTime ExpiresAt)>(
            "SELECT TokenHash, JwtId, FamilyId, ExpiresAt FROM auth.RefreshTokens WHERE UserId = @id;",
            new { id = user.Id });

        // The hash, never the token; and the row is tied to the access token issued with it.
        Assert.Equal(result.Tokens.Refresh.TokenHash, stored.TokenHash);
        Assert.NotEqual(result.Tokens.Refresh.Token, stored.TokenHash);
        Assert.Equal(result.Tokens.Access.JwtId, stored.JwtId);
        Assert.NotEqual(Guid.Empty, stored.FamilyId);
        Assert.Equal(result.Tokens.Refresh.ExpiresAt.UtcDateTime, stored.ExpiresAt, TimeSpan.FromMilliseconds(1));

        Assert.NotNull((await ReadUserAsync(user.Id)).LastLoginAt);

        var attempt = Assert.Single(await ReadAttemptsAsync(user.Phone));
        Assert.True(attempt.Succeeded);
        Assert.Equal(user.Id, attempt.UserId);
    }

    [Fact]
    public async Task SignsInByEmailWhateverItsCaseOrSurroundingSpace()
    {
        var email = $"Dana.Donor.{Guid.NewGuid():N}@Example.com";
        await CreateUserAsync(email: email);

        var result = await SignInAsync($"  {email.ToLowerInvariant()} ", Password);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AWrongPasswordIsRefused_CountedAndRecordedAgainstTheAccount()
    {
        var user = await CreateUserAsync();

        var result = await SignInAsync(user.Phone, WrongPassword);

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Null(result.Tokens);
        Assert.Equal(1, (await ReadUserAsync(user.Id)).AccessFailedCount);

        var attempt = Assert.Single(await ReadAttemptsAsync(user.Phone));
        Assert.False(attempt.Succeeded);
        Assert.Equal("InvalidPassword", attempt.FailureReason);
        Assert.Equal(user.Id, attempt.UserId);
    }

    [Fact]
    public async Task AnUnknownLoginGetsExactlyTheAnswerAWrongPasswordGets()
    {
        var user = await CreateUserAsync();
        var unknownPhone = "+9471" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

        var wrongPassword = await SignInAsync(user.Phone, WrongPassword);
        var unknown = await SignInAsync(unknownPhone, Password);

        Assert.Equal(wrongPassword, unknown);

        var attempt = Assert.Single(await ReadAttemptsAsync(unknownPhone));
        Assert.Null(attempt.UserId);
        Assert.Equal("UnknownLogin", attempt.FailureReason);
    }

    [Fact]
    public async Task ALoginThatIsNeitherPhoneNorEmailIsNotStoredAsTyped()
    {
        // The usual cause is a password typed into the login box. Storing it would put a plaintext
        // password in the history table.
        var typed = $"Secret!{Guid.NewGuid():N}";

        var result = await SignInAsync(typed, Password);

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Empty(await ReadAttemptsAsync(typed));
    }

    [Fact]
    public async Task AnAccountWithNoPasswordAnswersLikeAnUnknownOne()
    {
        var user = await CreateUserAsync(withPassword: false);

        var result = await SignInAsync(user.Phone, Password);

        Assert.Equal(SignInFailure.InvalidCredentials, result.Failure);
        Assert.Equal("NoPassword", Assert.Single(await ReadAttemptsAsync(user.Phone)).FailureReason);
    }

    [Fact]
    public async Task FiveFailuresLockTheAccountForFifteenMinutes_AndThenTheRightPasswordIsRefused()
    {
        var user = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(SignInFailure.InvalidCredentials, (await SignInAsync(user.Phone, WrongPassword)).Failure);
        }

        var state = await ReadUserAsync(user.Id);
        Assert.NotNull(state.LockoutEnd);
        Assert.InRange(state.LockoutEnd.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Equal(0, state.AccessFailedCount);

        var withRightPassword = await SignInAsync(user.Phone, Password);

        Assert.Equal(SignInFailure.LockedOut, withRightPassword.Failure);
        Assert.Null(withRightPassword.Tokens);

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.RefreshTokens WHERE UserId = @id;", new { id = user.Id }));
    }

    [Fact]
    public async Task TheLockLiftsOnceItsTimeHasPassed()
    {
        var user = await CreateUserAsync();
        await SetLockoutEndAsync(user.Id, TimeSpan.FromMinutes(-1));

        var result = await SignInAsync(user.Phone, Password);

        Assert.True(result.Succeeded);
        Assert.Null((await ReadUserAsync(user.Id)).LockoutEnd);
    }

    [Fact]
    public async Task ASuccessResetsTheCount_SoFourFailuresEitherSideOfItDoNotLock()
    {
        // Plan 5.8: one of the three duties A1 took over from UserManager. Without the reset, failures
        // from different days accumulate until a user typing the right password is locked out.
        var user = await CreateUserAsync();

        for (var i = 0; i < 4; i++)
        {
            await SignInAsync(user.Phone, WrongPassword);
        }

        Assert.True((await SignInAsync(user.Phone, Password)).Succeeded);

        for (var i = 0; i < 4; i++)
        {
            await SignInAsync(user.Phone, WrongPassword);
        }

        Assert.Null((await ReadUserAsync(user.Id)).LockoutEnd);
        Assert.True((await SignInAsync(user.Phone, Password)).Succeeded);
    }

    [Fact]
    public async Task ConcurrentWrongPasswordsAreEveryOneCounted()
    {
        // Five guesses at once. A read-then-write count would see 0 five times and store 1, and the
        // lockout could then be outrun by guessing in bursts. One UPDATE per failure cannot be.
        var user = await CreateUserAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SignInAsync(user.Phone, WrongPassword)));

        Assert.All(attempts, a => Assert.Equal(SignInFailure.InvalidCredentials, a.Failure));
        Assert.NotNull((await ReadUserAsync(user.Id)).LockoutEnd);
        Assert.Equal(SignInFailure.LockedOut, (await SignInAsync(user.Phone, Password)).Failure);
    }

    [Fact]
    public async Task ASuspendedAccountIsReportedOnlyToSomeoneWithItsPassword()
    {
        var user = await CreateUserAsync(status: "Suspended");

        Assert.Equal(SignInFailure.InvalidCredentials, (await SignInAsync(user.Phone, WrongPassword)).Failure);
        Assert.Equal(SignInFailure.NotAllowed, (await SignInAsync(user.Phone, Password)).Failure);
    }

    [Fact]
    public async Task ActsForTheOnlyInstitutionTheUserHolds()
    {
        var user = await CreateUserAsync(userType: "InstitutionStaff");
        await AssignRoleAsync(user.Id, database.AlphaInstitutionId);

        var result = await SignInAsync(user.Phone, Password);

        Assert.True(result.Succeeded);
        Assert.Equal(database.AlphaInstitutionId, InstitutionClaim(result));
        Assert.Equal("alpha-temple", Assert.Single(result.Institutions).Slug);
    }

    [Fact]
    public async Task ActsForNoneWhenTheUserHoldsSeveral_AndListsThemAll()
    {
        var user = await CreateUserAsync(userType: "InstitutionStaff");
        await AssignRoleAsync(user.Id, database.AlphaInstitutionId);
        await AssignRoleAsync(user.Id, database.BetaInstitutionId);

        var result = await SignInAsync(user.Phone, Password);

        Assert.True(result.Succeeded);
        Assert.Null(InstitutionClaim(result));
        Assert.Equal(["alpha-temple", "beta-temple"], result.Institutions.Select(i => i.Slug));
    }

    [Fact]
    public async Task IgnoresARoleWhoseValidityHasEnded()
    {
        var user = await CreateUserAsync(userType: "InstitutionStaff");
        await AssignRoleAsync(user.Id, database.AlphaInstitutionId, validTo: DateTime.UtcNow.AddDays(-1));

        var result = await SignInAsync(user.Phone, Password);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Institutions);
        Assert.Null(InstitutionClaim(result));
    }

    [Theory]
    [InlineData("PlatformStaff", true)]
    [InlineData("Donor", false)]
    [InlineData("InstitutionStaff", false)]
    public async Task OnlyPlatformStaffGetThePlatformFlag(string userType, bool expected)
    {
        var user = await CreateUserAsync(userType: userType);

        var result = await SignInAsync(user.Phone, Password);

        var jwt = new JsonWebToken(result.Tokens!.Access.Token);
        Assert.Equal(expected, jwt.GetPayloadValue<bool>(SethikaClaimTypes.IsPlatform));
    }

    [Fact]
    public async Task RehashesAPasswordStoredAtAnOlderIterationCount()
    {
        // Identity's own default. A hash made before AddSethikaAccess raised the count must move to
        // 210 000 at the next sign-in, the only moment the plaintext is available.
        var legacy = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }));
        var user = await CreateUserAsync(passwordHash: legacy.HashPassword(new User(), Password));

        Assert.True((await SignInAsync(user.Phone, Password)).Succeeded);

        var stored = (await ReadUserAsync(user.Id)).PasswordHash!;
        Assert.Equal(210_000u, BinaryPrimitives.ReadUInt32BigEndian(Convert.FromBase64String(stored).AsSpan(5)));
        Assert.True((await SignInAsync(user.Phone, Password)).Succeeded);
    }

    [Fact]
    public async Task UnknownLoginsAndLockedAccountsTakeAsLongAsAWrongPassword()
    {
        // Equal answers are only half of hiding which accounts exist; the other half is equal time.
        // Skipping the hash for an unknown login, or for a locked account, makes that answer arrive
        // a PBKDF2-verification sooner - a gap of the order of 100 ms, far above the noise here. The
        // bound is loose on purpose: it is there to catch a skipped hash, not to measure jitter.
        var wrongUser = await CreateUserAsync();
        var lockedUser = await CreateUserAsync();
        await SetLockoutEndAsync(lockedUser.Id, TimeSpan.FromMinutes(10));

        await SignInAsync(wrongUser.Phone, WrongPassword); // warm-up: JIT, pool, dummy hash

        async Task<double> MedianMillisecondsAsync(Func<Task> attempt)
        {
            var samples = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                var started = Stopwatch.GetTimestamp();
                await attempt();
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            return samples.Order().ElementAt(1);
        }

        var wrong = await MedianMillisecondsAsync(() => SignInAsync(wrongUser.Phone, WrongPassword));
        var unknown = await MedianMillisecondsAsync(() => SignInAsync("+94719999999", Password));
        var locked = await MedianMillisecondsAsync(() => SignInAsync(lockedUser.Phone, Password));

        Assert.True(unknown >= wrong * 0.5, $"unknown login {unknown:F0} ms vs wrong password {wrong:F0} ms");
        Assert.True(locked >= wrong * 0.5, $"locked account {locked:F0} ms vs wrong password {wrong:F0} ms");
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// <c>POST /api/me/password</c> over HTTP, against real SQL Server and Redis: the other device's
/// session ends, this one continues, and a wrong current password counts toward sign-in's lockout.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class PasswordChangeEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "password-change-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";
    private const string NewPassword = "Pirith#Night2027";
    private const string WrongPassword = "Not-The-Password-1";

    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter()));
        });
    }

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

                DELETE FROM core.OutboxMessages WHERE AggregateType = 'User'
                    AND AggregateId IN (SELECT CONVERT(VARCHAR(50), Id) FROM @ids);
                UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class LoopbackConnectionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>What a browser holds after a session starts.</summary>
    private sealed record Session(string Refresh, string Csrf, string AccessToken, JsonElement Body);

    private sealed class UserState
    {
        public Guid Id { get; init; }
        public string SecurityStamp { get; init; } = null!;
        public int AccessFailedCount { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTime? PasswordChangedAt { get; init; }
    }

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        return client;
    }

    private async Task<string> CreateUserAsync(string status = "Active")
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, Email, NormalizedEmail, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), 'InstitutionStaff', N'Password Test', NULL, NULL, @phone, 1, @hash, 'en', @status);
            """,
            new { phone, hash, status });

        return phone;
    }

    private async Task AssignRoleAsync(string phone, int institutionId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DECLARE @roleId INT = (SELECT Id FROM auth.Roles WHERE InstitutionId = @institutionId AND Code = 'PASSWORD_TEST');
            IF @roleId IS NULL
            BEGIN
                INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
                VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'), 'PASSWORD_TEST', N'Password test', 0);
                SET @roleId = CAST(SCOPE_IDENTITY() AS INT);
            END;
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT Id, @roleId, @institutionId, 1 FROM auth.Users WHERE PhoneNumber = @phone;
            """,
            new { phone, institutionId });
    }

    private static Session Parse(HttpResponseMessage response, JsonElement body)
    {
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);

        return new Session(
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString(),
            body.GetProperty("accessToken").GetString()!,
            body);
    }

    private static async Task<Session> ParseAsync(HttpResponseMessage response) =>
        Parse(response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);

    private async Task<HttpResponseMessage> LogInAsync(string phone, string password)
    {
        using var client = NewClient();
        return await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password });
    }

    private async Task<Session> SignInAsync(string phone, string password = Password)
    {
        using var response = await LogInAsync(phone, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ParseAsync(response);
    }

    private async Task<HttpResponseMessage> ChangeAsync(Session? session, string currentPassword, string newPassword)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword }),
        };

        if (session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Posts to refresh with the cookies a browser holding <paramref name="session"/> would attach.</summary>
    private async Task<HttpResponseMessage> RefreshAsync(Session session)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        request.Headers.Add("X-CSRF-Token", session.Csrf);
        return await client.SendAsync(request);
    }

    private async Task<UserState> ReadUserAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<UserState>(
            """
            SELECT Id, SecurityStamp, AccessFailedCount, LockoutEnd, PasswordChangedAt
            FROM auth.Users WHERE PhoneNumber = @phone;
            """,
            new { phone });
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    // ------------------------------------------------------------------ the 5.8 box

    [Fact]
    public async Task ChangingThePasswordEndsEveryOtherSession_AndKeepsThisDeviceSignedIn()
    {
        // Plan 5.8 / A1: changing a password rotates SecurityStamp, and every refresh token issued
        // before it is refused. The phone stands in for another device, signed in before the change.
        var phone = await CreateUserAsync();
        var laptop = await SignInAsync(phone);
        var otherDevice = await SignInAsync(phone);
        var before = await ReadUserAsync(phone);

        using var response = await ChangeAsync(laptop, Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var renewed = await ParseAsync(response);
        Assert.NotEqual(laptop.Refresh, renewed.Refresh);
        Assert.NotEqual(laptop.Csrf, renewed.Csrf);

        var after = await ReadUserAsync(phone);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.NotNull(after.PasswordChangedAt);

        // The other device: its refresh token was issued before the change.
        using (var other = await RefreshAsync(otherDevice))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        }

        // This device's old cookie went with it; the one the response set is what keeps it signed in.
        using (var oldCookie = await RefreshAsync(laptop))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
        }

        using (var newCookie = await RefreshAsync(renewed))
        {
            Assert.Equal(HttpStatusCode.OK, newCookie.StatusCode);
        }

        using (var oldPassword = await LogInAsync(phone, Password))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        }

        using (var newPassword = await LogInAsync(phone, NewPassword))
        {
            Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        }
    }

    [Fact]
    public async Task TheRevocationIsRecorded_AndTheOutboxRowHoldsOnlyTheUserId()
    {
        var phone = await CreateUserAsync();
        var laptop = await SignInAsync(phone);
        await SignInAsync(phone); // another device

        using var response = await ChangeAsync(laptop, Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ReadUserAsync(phone);
        await using var connection = new SqlConnection(database.ConnectionString);

        var reasons = (await connection.QueryAsync<string?>(
            "SELECT RevokedReason FROM auth.RefreshTokens WHERE UserId = @id AND TokenHash <> @renewed;",
            new { id = user.Id, renewed = Hash((await ParseAsync(response)).Refresh) })).ToList();
        Assert.Equal(2, reasons.Count);
        Assert.All(reasons, reason => Assert.Equal("PasswordChanged", reason));

        var outbox = (await connection.QueryAsync<(string EventType, string AggregateId, string Payload)>(
            "SELECT EventType, AggregateId, Payload FROM core.OutboxMessages WHERE AggregateType = 'User' AND AggregateId = @id;",
            new { id = user.Id.ToString() })).Single();

        Assert.Equal("PasswordChanged", outbox.EventType);
        Assert.Equal(
            ["userId"],
            JsonDocument.Parse(outbox.Payload).RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(phone[1..], outbox.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNewSessionKeepsActingForTheInstitutionTheDeviceHadChosen()
    {
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        await AssignRoleAsync(phone, database.BetaInstitutionId);
        var session = await SignInAsync(phone);

        Session switched;
        using (var client = NewClient())
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/switch-institution")
        {
            Content = JsonContent.Create(new { institution = "beta-temple" }),
        })
        {
            request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
            request.Headers.Add("X-CSRF-Token", session.Csrf);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            switched = await ParseAsync(response);
        }

        using var changed = await ChangeAsync(switched, Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("beta-temple", (await ParseAsync(changed)).Body.GetProperty("activeInstitution").GetString());
    }

    // ------------------------------------------------------------------ the current password

    [Fact]
    public async Task FiveWrongCurrentPasswordsLockTheAccount_ExactlyAsFiveWrongSignInsWould()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        var unlockedAnswer = "";
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await ChangeAsync(session, WrongPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal("password_incorrect", await CodeOf(wrong));
            unlockedAnswer = await wrong.Content.ReadAsStringAsync();
        }

        var state = await ReadUserAsync(phone);
        Assert.NotNull(state.LockoutEnd);
        Assert.InRange(state.LockoutEnd.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));

        // Locked: the right current password is refused with the answer a wrong one got before the
        // lock, byte for byte, and changes nothing. Compared with an unlocked answer, because while
        // locked every password gets the same answer whatever that answer is.
        using var locked = await ChangeAsync(session, Password, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Equal(unlockedAnswer, await locked.Content.ReadAsStringAsync());
        Assert.Equal(state.SecurityStamp, (await ReadUserAsync(phone)).SecurityStamp);

        // And sign-in is locked too: one lockout, not two.
        using var signIn = await LogInAsync(phone, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPasswordsAndWrongSignInsCountTowardOneLockout()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        for (var i = 0; i < 3; i++)
        {
            using var signIn = await LogInAsync(phone, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        }

        for (var i = 0; i < 2; i++)
        {
            using var change = await ChangeAsync(session, WrongPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        }

        Assert.NotNull((await ReadUserAsync(phone)).LockoutEnd);

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(5, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.LoginHistory WHERE LoginName = @phone AND FailureReason = 'InvalidPassword';",
            new { phone }));
    }

    [Fact]
    public async Task ASuccessfulChangeResetsTheFailureCount()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        for (var i = 0; i < 4; i++)
        {
            using var wrong = await ChangeAsync(session, WrongPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        Assert.Equal(4, (await ReadUserAsync(phone)).AccessFailedCount);

        using var changed = await ChangeAsync(session, Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var state = await ReadUserAsync(phone);
        Assert.Equal(0, state.AccessFailedCount);
        Assert.Null(state.LockoutEnd);
    }

    // ------------------------------------------------------------------ the new password

    [Fact]
    public async Task AWeakNewPasswordGetsSignupsPolicyError_AndChangesNothing()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);
        var before = await ReadUserAsync(phone);

        using var response = await ChangeAsync(session, Password, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("password_rejected", body.GetProperty("code").GetString());
        Assert.Contains("TooShort", body.GetProperty("failures").EnumerateArray().Select(f => f.GetString()));

        Assert.Equal(before.SecurityStamp, (await ReadUserAsync(phone)).SecurityStamp);
        using var refresh = await RefreshAsync(session);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task ANewPasswordContainingTheUsersOwnPhoneNumberIsRejected()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        using var response = await ChangeAsync(session, Password, "Dana!" + phone[^7..] + "x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var failures = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("failures").EnumerateArray().Select(f => f.GetString());
        Assert.Equal(["ContainsPersonalData"], failures);
    }

    [Fact]
    public async Task TheCurrentPasswordAsTheNewOneIsPasswordUnchanged()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);
        var before = await ReadUserAsync(phone);

        using var response = await ChangeAsync(session, Password, Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password_unchanged", await CodeOf(response));
        Assert.Equal(before.SecurityStamp, (await ReadUserAsync(phone)).SecurityStamp);
    }

    [Fact]
    public async Task AWrongCurrentPasswordIsReportedBeforeAnythingAboutTheNewOne()
    {
        // The policy compares against the user's own phone number. Answering it before the current
        // password is proved would let a token holder test what the number is.
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        using var probe = await ChangeAsync(session, WrongPassword, "Dana!" + phone[^7..] + "x");

        Assert.Equal(HttpStatusCode.BadRequest, probe.StatusCode);
        Assert.Equal("password_incorrect", await CodeOf(probe));
    }

    // ------------------------------------------------------------------ who may call it

    [Fact]
    public async Task WithoutAnAccessTokenIs401()
    {
        using var response = await ChangeAsync(session: null, Password, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The bearer challenge shows the authorization requirement refused it, not only the
        // handler's own check behind it.
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task ASuspendedAccountWithAStillValidTokenCannotChangeItsPassword()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @phone;", new { phone });
        }

        using var response = await ChangeAsync(session, Password, NewPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_not_active", await CodeOf(response));
    }

    [Fact]
    public async Task IsRateLimitedPerUser_NotPerAddress()
    {
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone);

        // "Unchanged" needs the right current password, so these never reach the lockout. Each request
        // comes from a different address, so only a per-user limit can stop the eleventh.
        for (var i = 0; i < 10; i++)
        {
            using var response = await ChangeAsync(session, Password, Password);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var limited = await ChangeAsync(session, Password, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        var someoneElse = await SignInAsync(await CreateUserAsync());
        using var unaffected = await ChangeAsync(someoneElse, Password, Password);
        Assert.Equal(HttpStatusCode.BadRequest, unaffected.StatusCode);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

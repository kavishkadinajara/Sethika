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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Refresh rotation, reuse detection, institution switching and logout over HTTP, with the refresh
/// and CSRF cookies sent the way a browser would send them, against real SQL Server and Redis.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class SessionEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "session-endpoint-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";

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

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        return client;
    }

    private async Task<string> CreateUserAsync(string userType = "InstitutionStaff")
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), @userType, N'Session Test', @phone, 1, @hash, 'en', 'Active');
            """,
            new { phone, hash, userType });

        return phone;
    }

    private async Task AssignRoleAsync(string phone, int institutionId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DECLARE @roleId INT = (SELECT Id FROM auth.Roles WHERE InstitutionId = @institutionId AND Code = 'SESSION_TEST');
            IF @roleId IS NULL
            BEGIN
                INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
                VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'), 'SESSION_TEST', N'Session test', 0);
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

    private async Task<Session> LogInAsync(string phone)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return Parse(response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
    }

    /// <summary>Posts to a session endpoint with the cookies a browser would attach.</summary>
    private async Task<HttpResponseMessage> PostAsync(
        string path,
        Session session,
        object? body = null,
        string? csrfHeader = "(from cookie)")
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/" + path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };

        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        if (csrfHeader is not null)
        {
            request.Headers.Add("X-CSRF-Token", csrfHeader == "(from cookie)" ? session.Csrf : csrfHeader);
        }

        return await client.SendAsync(request);
    }

    private async Task<Session> RefreshAsync(Session session)
    {
        using var response = await PostAsync("refresh", session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parse(response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
    }

    private sealed class TokenRow
    {
        public long Id { get; init; }
        public Guid FamilyId { get; init; }
        public int? ActiveInstitutionId { get; init; }
        public DateTime? RevokedAt { get; init; }
        public string? RevokedReason { get; init; }
        public long? ReplacedByTokenId { get; init; }
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<TokenRow> RowAsync(string refreshToken)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<TokenRow>(
            """
            SELECT Id, FamilyId, ActiveInstitutionId, RevokedAt, RevokedReason, ReplacedByTokenId
            FROM auth.RefreshTokens WHERE TokenHash = @hash;
            """,
            new { hash = Hash(refreshToken) });
    }

    private async Task<List<TokenRow>> FamilyAsync(Guid familyId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<TokenRow>(
            """
            SELECT Id, FamilyId, ActiveInstitutionId, RevokedAt, RevokedReason, ReplacedByTokenId
            FROM auth.RefreshTokens WHERE FamilyId = @familyId ORDER BY Id;
            """,
            new { familyId })];
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>Waits until <paramref name="expected"/> lock requests on <c>auth.RefreshTokens</c> are queued.</summary>
    private async Task WaitForLockWaitsOnRefreshTokensAsync(int expected)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            var waiting = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.resource_database_id = DB_ID()
                  AND l.request_status = 'WAIT'
                  AND p.object_id = OBJECT_ID(N'auth.RefreshTokens');
                """);

            if (waiting >= expected)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Only {waiting} of {expected} requests reached the claim.");
            await Task.Delay(50);
        }
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    /// <summary>
    /// The answer for a session that is over: 401 <c>session_expired</c>, both cookies cleared,
    /// and no token in the body.
    /// </summary>
    private static async Task AssertEndedSessionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", await CodeOf(response));
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);
        Assert.All(["sethika-refresh", "sethika-csrf"], name =>
            Assert.Contains(cookies, c => c.Name == name && c.Expires < DateTimeOffset.UtcNow && c.Value.Length == 0));
    }

    // ------------------------------------------------------------------ rotation

    [Fact]
    public async Task RefreshRotatesWithinTheFamily_AndTheOldTokenStopsWorking()
    {
        // Plan 5.8: refresh rotates, and the old token stops working.
        var first = await LogInAsync(await CreateUserAsync());

        var second = await RefreshAsync(first);

        Assert.NotEqual(first.Refresh, second.Refresh);

        var old = await RowAsync(first.Refresh);
        var successor = await RowAsync(second.Refresh);
        Assert.Equal("Rotated", old.RevokedReason);
        Assert.Equal(successor.Id, old.ReplacedByTokenId);
        Assert.Equal(old.FamilyId, successor.FamilyId);
        Assert.Null(successor.RevokedAt);

        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/permissions")).StatusCode);

        // Inside the grace period, so refused as a race rather than a replay - but refused.
        using var again = await PostAsync("refresh", first);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.DoesNotContain("accessToken", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayingARotatedTokenRevokesTheWholeFamily()
    {
        // Plan 5.8 - the security test that matters most. The rotation is pushed back past the grace
        // period, so the replay cannot be a second tab racing the first: it is a copy.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        await ExecuteAsync(
            "UPDATE auth.RefreshTokens SET RevokedAt = DATEADD(MINUTE, -1, RevokedAt) WHERE TokenHash = @hash;",
            new { hash = Hash(first.Refresh) });

        using var replay = await PostAsync("refresh", first);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The legitimate holder's current token dies with it: nobody can tell whose copy was whose.
        using var legitimate = await PostAsync("refresh", second);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);

        var family = await FamilyAsync((await RowAsync(first.Refresh)).FamilyId);
        Assert.All(family, t => Assert.NotNull(t.RevokedAt));
        Assert.Equal("ReuseDetected", family[^1].RevokedReason);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodIsRefusedButTheFamilySurvives()
    {
        // Two tabs refreshing at once both send the old cookie. The late one gets nothing, but the
        // user is not signed out of everything for it - and its response must not clear the
        // cookies, or it could delete the successor the other tab was just given.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        using var late = await PostAsync("refresh", first);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("session_superseded", await CodeOf(late));
        Assert.False(late.Headers.Contains("Set-Cookie"));

        await RefreshAsync(second);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodAfterLogoutIsAnEndedSession()
    {
        // The walk-through that found it: log out, then send the old cookie within ten seconds.
        // No tab won a race here - the session is over - so the answer is the one every ended
        // session gets, and the cookies go with it.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        using var logout = await PostAsync("logout", second);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var late = await PostAsync("refresh", first);

        await AssertEndedSessionAsync(late);
        Assert.Equal(2, (await FamilyAsync((await RowAsync(first.Refresh)).FamilyId)).Count);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodAfterLogoutEverywhereIsAnEndedSession()
    {
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        using var logoutAll = await PostAsync("logout-all", second);
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);

        using var late = await PostAsync("refresh", first);

        await AssertEndedSessionAsync(late);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodAfterTheSuccessorExpiredIsAnEndedSession()
    {
        // Expiry on its own, with nothing revoked: a liveness check that looked only at RevokedAt
        // would still call this a race.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        await ExecuteAsync(
            "UPDATE auth.RefreshTokens SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE TokenHash = @hash;",
            new { hash = Hash(second.Refresh) });

        using var late = await PostAsync("refresh", first);

        await AssertEndedSessionAsync(late);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodAfterTheStampChangedIsAnEndedSession()
    {
        // The stamp alone, with the successor neither revoked nor expired - what a password change
        // that missed this family's revocation would leave behind.
        var phone = await CreateUserAsync();
        var first = await LogInAsync(phone);
        await RefreshAsync(first);

        await ExecuteAsync("UPDATE auth.Users SET SecurityStamp = 'changed' WHERE PhoneNumber = @phone;", new { phone });

        using var late = await PostAsync("refresh", first);

        await AssertEndedSessionAsync(late);
    }

    [Fact]
    public async Task AReplayWithinTheGracePeriodFollowsTheFamilyPastAFurtherRotation()
    {
        // Tab one rotates A to B and, still inside A's grace, B to C. Tab two's late A must still be
        // a race: answering it as an ended session would clear the shared cookie jar, deleting C.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);
        var third = await RefreshAsync(second);

        using var late = await PostAsync("refresh", first);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("session_superseded", await CodeOf(late));
        Assert.False(late.Headers.Contains("Set-Cookie"));

        await RefreshAsync(third);
    }

    [Fact]
    public async Task ParallelRefreshesWithOneTokenLetExactlyOneThrough()
    {
        // Plan 5.8. The conditional UPDATE is the lock: of ten requests at once, one finds the token
        // still unrevoked, and only one successor is ever written.
        //
        // Left alone, ten requests rarely overlap - the first is often done before the rest have
        // read the token - so the test makes them. It holds an update lock on the token's row. That
        // lets every request read the token as live (an update lock does not block readers) but
        // stops each one at its claim. Once all ten wait there, the lock goes.
        var session = await LogInAsync(await CreateUserAsync());
        var tokenId = (await RowAsync(session.Refresh)).Id;

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM auth.RefreshTokens WITH (UPDLOCK, ROWLOCK) WHERE Id = @tokenId;", new { tokenId }, hold);

        var requests = Enumerable.Range(0, 10).Select(_ => PostAsync("refresh", session)).ToList();
        await WaitForLockWaitsOnRefreshTokensAsync(expected: 10);
        await hold.RollbackAsync();

        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var losers = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        Assert.All(losers, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.All(losers, r => Assert.False(r.Headers.Contains("Set-Cookie")));
        Assert.Equal(2, (await FamilyAsync((await RowAsync(session.Refresh)).FamilyId)).Count);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task ARefreshThatLosesItsClaimToALogoutIsAnEndedSession()
    {
        // The claim's other loser: not a tab that rotated first, but a logout revoking the token
        // while the refresh was on its way to the claim. Nothing superseded it, so it is not 409.
        //
        // Same lock as the parallel test, released once the logout and then the refresh queue
        // behind it, so the logout is granted the row first and the order is deterministic.
        var session = await LogInAsync(await CreateUserAsync());
        var tokenId = (await RowAsync(session.Refresh)).Id;

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM auth.RefreshTokens WITH (UPDLOCK, ROWLOCK) WHERE Id = @tokenId;", new { tokenId }, hold);

        var logout = PostAsync("logout", session);
        await WaitForLockWaitsOnRefreshTokensAsync(expected: 1);
        var refresh = PostAsync("refresh", session);
        await WaitForLockWaitsOnRefreshTokensAsync(expected: 2);
        await hold.RollbackAsync();

        using var loggedOut = await logout;
        using var late = await refresh;

        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        Assert.Equal("Logout", (await RowAsync(session.Refresh)).RevokedReason);
        await AssertEndedSessionAsync(late);
        Assert.Single(await FamilyAsync((await RowAsync(session.Refresh)).FamilyId));
    }

    // ------------------------------------------------------------------ what ends a session

    [Fact]
    public async Task WithoutTheCsrfHeaderNothingIsRotated()
    {
        var session = await LogInAsync(await CreateUserAsync());

        using var missing = await PostAsync("refresh", session, csrfHeader: null);
        using var wrong = await PostAsync("refresh", session, csrfHeader: "not-the-cookie");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("csrf_failed", await CodeOf(wrong));
        Assert.Null((await RowAsync(session.Refresh)).RevokedAt);

        await RefreshAsync(session);
    }

    [Fact]
    public async Task AChangedSecurityStampEndsTheSession()
    {
        // What a password change will do: the stamp moves, and every token issued before it is dead
        // even if nothing revoked it directly.
        var phone = await CreateUserAsync();
        var session = await LogInAsync(phone);

        await ExecuteAsync("UPDATE auth.Users SET SecurityStamp = 'changed' WHERE PhoneNumber = @phone;", new { phone });

        using var response = await PostAsync("refresh", session);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SecurityStampChanged", (await RowAsync(session.Refresh)).RevokedReason);
    }

    [Fact]
    public async Task AnExpiredRefreshTokenIsRefused()
    {
        var session = await LogInAsync(await CreateUserAsync());
        await ExecuteAsync(
            "UPDATE auth.RefreshTokens SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE TokenHash = @hash;",
            new { hash = Hash(session.Refresh) });

        using var response = await PostAsync("refresh", session);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASuspendedAccountsSessionEnds()
    {
        var phone = await CreateUserAsync();
        var session = await LogInAsync(phone);
        await ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @phone;", new { phone });

        using var response = await PostAsync("refresh", session);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AccountNotActive", (await RowAsync(session.Refresh)).RevokedReason);
    }

    [Fact]
    public async Task LogoutEndsTheFamilyAndRemovesTheCookies()
    {
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        using var logout = await PostAsync("logout", second);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = SetCookieHeaderValue.ParseStrictList([.. logout.Headers.GetValues("Set-Cookie")]);
        Assert.All(["sethika-refresh", "sethika-csrf"], name =>
            Assert.Contains(cleared, c => c.Name == name && c.Expires < DateTimeOffset.UtcNow));

        Assert.All(await FamilyAsync((await RowAsync(second.Refresh)).FamilyId), t => Assert.NotNull(t.RevokedAt));
        Assert.Equal("Logout", (await RowAsync(second.Refresh)).RevokedReason);

        using var afterwards = await PostAsync("refresh", second);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task LogoutEverywhereEndsEverySessionAndRotatesTheStamp()
    {
        // Two devices, two families. Both mechanisms are asserted separately, because each would make
        // the refresh below fail on its own - a test of the refresh alone could not tell if one
        // had quietly stopped working.
        var phone = await CreateUserAsync();
        var laptop = await LogInAsync(phone);
        var phoneSession = await LogInAsync(phone);

        await using var connection = new SqlConnection(database.ConnectionString);
        var stampBefore = await connection.ExecuteScalarAsync<string>(
            "SELECT SecurityStamp FROM auth.Users WHERE PhoneNumber = @phone;", new { phone });

        using var response = await PostAsync("logout-all", laptop);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.NotEqual(stampBefore, await connection.ExecuteScalarAsync<string>(
            "SELECT SecurityStamp FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }));
        Assert.Equal("LogoutEverywhere", (await RowAsync(laptop.Refresh)).RevokedReason);
        Assert.Equal("LogoutEverywhere", (await RowAsync(phoneSession.Refresh)).RevokedReason);

        using var other = await PostAsync("refresh", phoneSession);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    // ------------------------------------------------------------------ institutions

    [Fact]
    public async Task SwitchingToAnInstitutionTheUserHoldsRotatesIntoIt()
    {
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        await AssignRoleAsync(phone, database.BetaInstitutionId);
        var session = await LogInAsync(phone);
        Assert.Equal(JsonValueKind.Null, session.Body.GetProperty("activeInstitution").ValueKind);

        using var response = await PostAsync("switch-institution", session, new { institution = "beta-temple" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var switched = Parse(response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
        Assert.Equal("beta-temple", switched.Body.GetProperty("activeInstitution").GetString());
        Assert.Equal(
            database.BetaInstitutionId,
            new JsonWebToken(switched.AccessToken).GetPayloadValue<int>(SethikaClaimTypes.InstitutionId));
        Assert.Equal(database.BetaInstitutionId, (await RowAsync(switched.Refresh)).ActiveInstitutionId);
    }

    [Fact]
    public async Task SwitchingToAnInstitutionTheUserDoesNotHoldIsForbiddenAndIssuesNothing()
    {
        // Plan 5.8: a user who can name an institution must not thereby gain it.
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        var session = await LogInAsync(phone);

        using var response = await PostAsync("switch-institution", session, new { institution = "beta-temple" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("institution_not_available", await CodeOf(response));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Null((await RowAsync(session.Refresh)).RevokedAt);

        await RefreshAsync(session);   // the session itself is untouched
    }

    [Fact]
    public async Task ARevokedRoleTakesItsInstitutionOutOfTheNextToken()
    {
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        var session = await LogInAsync(phone);
        Assert.Equal("alpha-temple", session.Body.GetProperty("activeInstitution").GetString());

        await ExecuteAsync(
            "UPDATE ur SET IsActive = 0 FROM auth.UserRoles ur JOIN auth.Users u ON u.Id = ur.UserId WHERE u.PhoneNumber = @phone;",
            new { phone });

        var refreshed = await RefreshAsync(session);

        Assert.Equal(JsonValueKind.Null, refreshed.Body.GetProperty("activeInstitution").ValueKind);
        Assert.DoesNotContain(new JsonWebToken(refreshed.AccessToken).Claims, c => c.Type == SethikaClaimTypes.InstitutionId);
    }

    [Fact]
    public async Task NoRefreshCookieIsNoSession()
    {
        var session = await LogInAsync(await CreateUserAsync());

        using var response = await PostAsync("refresh", session with { Refresh = string.Empty });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", await CodeOf(response));
    }
}

using System.Collections.Concurrent;
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
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Mobile sessions (plan F3) over HTTP, against real SQL Server and Redis: the refresh token travels
/// in the body under <c>/api/auth/mobile</c>, never in a cookie, and every session rule the web has
/// still holds.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class MobileSessionEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "mobile-session-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";
    private const string AppUserAgent = "Sethika/1.0 (Android 15)";

    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
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
            builder.UseSetting("Otp:UndisclosedSendDuration", TimeSpan.Zero.ToString("c", CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms));
                services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter());
            });
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
                DELETE FROM auth.PasswordHistory WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids) OR LoginName IN @phones;
                DELETE FROM auth.UserConsents  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.DonorProfiles WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                DELETE FROM auth.Roles         WHERE Code = 'MOBILE_TEST';
                """,
                new { phones = _phones });
        }

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((phoneNumber, message));
            return Task.CompletedTask;
        }
    }

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

    /// <summary>What the app holds after a session starts.</summary>
    private sealed record MobileSession(string Refresh, string AccessToken, JsonElement Body);

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppUserAgent);
        return client;
    }

    private string NewPhone()
    {
        var phone = "+9478" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private async Task<string> CreateUserAsync()
    {
        var phone = NewPhone();
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), 'InstitutionStaff', N'Mobile Test', @phone, 1, @hash, 'en', 'Active');
            """,
            new { phone, hash });

        return phone;
    }

    private async Task AssignRoleAsync(string phone, int institutionId)
    {
        await ExecuteAsync(
            """
            DECLARE @roleId INT = (SELECT Id FROM auth.Roles WHERE InstitutionId = @institutionId AND Code = 'MOBILE_TEST');
            IF @roleId IS NULL
            BEGIN
                INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
                VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'), 'MOBILE_TEST', N'Mobile test', 0);
                SET @roleId = CAST(SCOPE_IDENTITY() AS INT);
            END;
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT Id, @roleId, @institutionId, 1 FROM auth.Users WHERE PhoneNumber = @phone;
            """,
            new { phone, institutionId });
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("No row.");
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString();

    /// <summary>A mobile answer never carries a cookie, whatever it says.</summary>
    private static void AssertNoCookies(HttpResponseMessage response) =>
        Assert.False(response.Headers.Contains("Set-Cookie"), "A mobile route set a cookie.");

    private static async Task<MobileSession> ParseAsync(HttpResponseMessage response)
    {
        AssertNoCookies(response);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var body = await JsonAsync(response);
        return new MobileSession(
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("accessToken").GetString()!,
            body);
    }

    private async Task<MobileSession> LogInAsync(string phone)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/mobile/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ParseAsync(response);
    }

    private async Task<HttpResponseMessage> PostSessionAsync(string path, object body, string? cookie = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mobile/session/" + path)
        {
            Content = JsonContent.Create(body),
        };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request);
    }

    /// <summary>A browser's refresh, with its cookies and CSRF header.</summary>
    private async Task<HttpResponseMessage> WebRefreshAsync(string refresh, string csrf)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        request.Headers.Add("Cookie", $"sethika-refresh={refresh}; sethika-csrf={csrf}");
        request.Headers.Add("X-CSRF-Token", csrf);
        return await client.SendAsync(request);
    }

    private async Task<MobileSession> RefreshAsync(MobileSession session)
    {
        using var response = await PostSessionAsync("refresh", new { refreshToken = session.Refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ParseAsync(response);
    }

    private async Task<HttpStatusCode> CallWithAsync(string accessToken)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync("/api/me/permissions");
        return response.StatusCode;
    }

    // ------------------------------------------------------------------ starting a session

    [Fact]
    public async Task PasswordLoginReturnsTheRefreshTokenInTheBody_AndNoCookie()
    {
        var phone = await CreateUserAsync();

        var session = await LogInAsync(phone);

        Assert.Equal(
            ["accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "institutions", "activeInstitution"],
            session.Body.EnumerateObject().Select(p => p.Name));
        Assert.True(session.Body.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(session.AccessToken));

        // The same row the web would get, bound to the stamp, with the app named as the device.
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RefreshTokens t JOIN auth.Users u ON u.Id = t.UserId
            WHERE t.TokenHash = @hash AND t.SecurityStamp = u.SecurityStamp AND t.DeviceInfo = @device AND u.PhoneNumber = @phone;
            """,
            new { hash = Hash(session.Refresh), device = AppUserAgent, phone }));
    }

    [Fact]
    public async Task WebLoginStillSetsCookies_AndNeverPutsTheRefreshTokenInTheBody()
    {
        // The mobile route must not leak into the web's: a body token there would undo A2.
        var phone = await CreateUserAsync();

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);
        Assert.Single(cookies, c => c.Name == "sethika-refresh" && c.HttpOnly);
        Assert.DoesNotContain("refreshToken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPasswordIsTheSameAnswerAsOnTheWeb()
    {
        var phone = await CreateUserAsync();

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/mobile/login", new { login = phone, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await CodeOf(response));
        AssertNoCookies(response);
    }

    [Fact]
    public async Task ACodeSignsInOnMobile()
    {
        var phone = await CreateUserAsync();
        using var client = NewClient();

        using var requested = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "login" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var code = new string([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);

        using var response = await client.PostAsJsonAsync("/api/auth/mobile/otp/sign-in", new { phoneNumber = phone, code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(session.AccessToken));
    }

    [Fact]
    public async Task ADonorSignsUpOnMobile_AndTheFirstSessionIsInTheBody()
    {
        // The journey the app's signup screens take: code, verify, signup.
        var phone = NewPhone();
        using var client = NewClient();

        var documents = await client.GetFromJsonAsync<JsonElement[]>("/api/legal/current?language=en");
        Assert.NotNull(documents);

        using var requested = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "signup", language = "en" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var code = new string([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);

        using var verified = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, purpose = "signup", code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var ticket = (await JsonAsync(verified)).GetProperty("ticket").GetString();

        using var signUp = await client.PostAsJsonAsync("/api/auth/mobile/signup", new
        {
            phoneNumber = phone,
            verificationTicket = ticket,
            fullName = "Mobile Donor",
            password = Password,
            preferredLanguage = "en",
            acceptedDocumentHashes = documents.Select(d => d.GetProperty("contentSha256").GetString()),
        });

        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        var session = await ParseAsync(signUp);
        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(session.AccessToken));
    }

    // ------------------------------------------------------------------ rotation

    [Fact]
    public async Task RefreshRotatesWithinTheFamily_WithoutACsrfToken()
    {
        // No cookie is read here, so there is nothing for CSRF to protect and no header is asked for.
        var first = await LogInAsync(await CreateUserAsync());

        var second = await RefreshAsync(first);

        Assert.NotEqual(first.Refresh, second.Refresh);
        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(second.AccessToken));
        Assert.Equal(
            await ScalarAsync<Guid>("SELECT FamilyId FROM auth.RefreshTokens WHERE TokenHash = @hash;", new { hash = Hash(first.Refresh) }),
            await ScalarAsync<Guid>("SELECT FamilyId FROM auth.RefreshTokens WHERE TokenHash = @hash;", new { hash = Hash(second.Refresh) }));
    }

    [Fact]
    public async Task ASpentTokenWithinTheGracePeriodIsSuperseded()
    {
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        using var late = await PostSessionAsync("refresh", new { refreshToken = first.Refresh });

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("session_superseded", await CodeOf(late));
        AssertNoCookies(late);

        await RefreshAsync(second);
    }

    [Fact]
    public async Task ReplayingARotatedTokenRevokesTheWholeFamily()
    {
        // A copy of the token taken off the phone, used after the grace period.
        var first = await LogInAsync(await CreateUserAsync());
        var second = await RefreshAsync(first);

        await ExecuteAsync(
            "UPDATE auth.RefreshTokens SET RevokedAt = DATEADD(MINUTE, -1, RevokedAt) WHERE TokenHash = @hash;",
            new { hash = Hash(first.Refresh) });

        using var replay = await PostSessionAsync("refresh", new { refreshToken = first.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("session_expired", await CodeOf(replay));
        AssertNoCookies(replay);

        using var legitimate = await PostSessionAsync("refresh", new { refreshToken = second.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
    }

    [Fact]
    public async Task AMissingOrUnknownTokenIsAnEndedSession()
    {
        using var missing = await PostSessionAsync("refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("session_expired", await CodeOf(missing));
        AssertNoCookies(missing);

        using var unknown = await PostSessionAsync("refresh", new { refreshToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task TheMobileRoutesNeverReadTheWebCookie()
    {
        // A browser that holds a web session and is made to post here must get nothing for it: the
        // cookie is not the body token, so this would otherwise be a CSRF hole that skips the header.
        var phone = await CreateUserAsync();
        using var client = NewClient();
        using var web = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        var cookies = SetCookieHeaderValue.ParseStrictList([.. web.Headers.GetValues("Set-Cookie")]);
        var refresh = cookies.Single(c => c.Name == "sethika-refresh").Value.ToString();
        var csrf = cookies.Single(c => c.Name == "sethika-csrf").Value.ToString();
        var cookieHeader = $"sethika-refresh={refresh}; sethika-csrf={csrf}";

        using var stolen = await PostSessionAsync("refresh", new { }, cookieHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, stolen.StatusCode);

        using var logout = await PostSessionAsync("logout-all", new { }, cookieHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);

        // And the web session is untouched by either.
        using var webRefresh = await WebRefreshAsync(refresh, csrf);
        Assert.Equal(HttpStatusCode.OK, webRefresh.StatusCode);
    }

    // ------------------------------------------------------------------ institution and logout

    [Fact]
    public async Task SwitchingInstitutionRotatesOntoIt()
    {
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        await AssignRoleAsync(phone, database.BetaInstitutionId);
        var session = await LogInAsync(phone);

        var target = session.Body.GetProperty("institutions").EnumerateArray()
            .Select(i => i.GetProperty("slug").GetString())
            .First(slug => slug != session.Body.GetProperty("activeInstitution").GetString());

        using var switched = await PostSessionAsync("switch-institution", new { refreshToken = session.Refresh, institution = target });

        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var next = await ParseAsync(switched);
        Assert.Equal(target, next.Body.GetProperty("activeInstitution").GetString());

        using var blank = await PostSessionAsync("switch-institution", new { refreshToken = next.Refresh, institution = " " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("institution_required", await CodeOf(blank));
    }

    [Fact]
    public async Task LogoutEndsThisDevicesSession()
    {
        var session = await LogInAsync(await CreateUserAsync());

        using var logout = await PostSessionAsync("logout", new { refreshToken = session.Refresh });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        AssertNoCookies(logout);

        using var after = await PostSessionAsync("refresh", new { refreshToken = session.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task LogoutEverywhereFromThePhoneEndsTheBrowserSessionToo()
    {
        var phone = await CreateUserAsync();
        var phoneSession = await LogInAsync(phone);

        using var client = NewClient();
        using var web = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        var cookies = SetCookieHeaderValue.ParseStrictList([.. web.Headers.GetValues("Set-Cookie")]);

        using var logoutAll = await PostSessionAsync("logout-all", new { refreshToken = phoneSession.Refresh });
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);

        using var webRefresh = await WebRefreshAsync(
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, webRefresh.StatusCode);

        using var again = await PostSessionAsync("logout-all", new { refreshToken = phoneSession.Refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal("session_expired", await CodeOf(again));
    }

    // ------------------------------------------------------------------ changing the password (F39)

    private const string NewPassword = "Pirith#Night2027";

    private async Task<HttpResponseMessage> ChangeAsync(
        string? accessToken,
        string currentPassword,
        string newPassword,
        string path = "/api/auth/mobile/password")
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { currentPassword, newPassword }),
        };

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request);
    }

    private async Task<HttpStatusCode> LogInStatusAsync(string phone, string password)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/mobile/login", new { login = phone, password });
        return response.StatusCode;
    }

    private Task<string> StampOfAsync(string phone) =>
        ScalarAsync<string>("SELECT SecurityStamp FROM auth.Users WHERE PhoneNumber = @phone;", new { phone });

    [Fact]
    public async Task ChangingThePasswordAnswersWithANewSessionInTheBody_AndNoCookie()
    {
        var phone = await CreateUserAsync();
        var session = await LogInAsync(phone);
        var otherPhone = await LogInAsync(phone);

        using var response = await ChangeAsync(session.AccessToken, Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renewed = await ParseAsync(response);
        Assert.Equal(
            ["accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "institutions", "activeInstitution"],
            renewed.Body.EnumerateObject().Select(p => p.Name));
        Assert.NotEqual(session.Refresh, renewed.Refresh);

        // The old tokens are refused first: a late refresh from this phone must not take the new
        // session down with it, as a replayed token takes down its family.
        using (var old = await PostSessionAsync("refresh", new { refreshToken = session.Refresh }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
            Assert.Equal("session_expired", await CodeOf(old));
        }

        using (var other = await PostSessionAsync("refresh", new { refreshToken = otherPhone.Refresh }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(renewed.AccessToken));
        var next = await RefreshAsync(renewed);
        Assert.Equal(HttpStatusCode.OK, await CallWithAsync(next.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await LogInStatusAsync(phone, Password));
        Assert.Equal(HttpStatusCode.OK, await LogInStatusAsync(phone, NewPassword));
    }

    [Fact]
    public async Task AWrongCurrentPasswordChangesNothing()
    {
        var phone = await CreateUserAsync();
        var session = await LogInAsync(phone);
        var stamp = await StampOfAsync(phone);

        using var response = await ChangeAsync(session.AccessToken, "Not-The-Password-1", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password_incorrect", await CodeOf(response));
        AssertNoCookies(response);

        Assert.Equal(stamp, await StampOfAsync(phone));
        await RefreshAsync(session);
        Assert.Equal(HttpStatusCode.OK, await LogInStatusAsync(phone, Password));
    }

    [Fact]
    public async Task ChangingThePasswordWithoutAnAccessTokenIs401_ThoughTheRoutesAroundItAreAnonymous()
    {
        var phone = await CreateUserAsync();
        var stamp = await StampOfAsync(phone);

        using var response = await ChangeAsync(accessToken: null, Password, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The bearer challenge shows the authorization requirement refused it, not only the
        // handler's own check behind it.
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.Equal(stamp, await StampOfAsync(phone));
    }

    [Fact]
    public async Task ThePhoneAndTheWebSpendOnePasswordChangeBudget()
    {
        var phone = await CreateUserAsync();
        var session = await LogInAsync(phone);

        // "Unchanged" needs the right current password, so these never reach the lockout. Five on
        // each route: a budget per route would still have five left when the eleventh comes.
        for (var i = 0; i < 5; i++)
        {
            using var web = await ChangeAsync(session.AccessToken, Password, Password, "/api/me/password");
            Assert.Equal(HttpStatusCode.BadRequest, web.StatusCode);
            using var mobile = await ChangeAsync(session.AccessToken, Password, Password);
            Assert.Equal(HttpStatusCode.BadRequest, mobile.StatusCode);
        }

        using var mobileLimited = await ChangeAsync(session.AccessToken, Password, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, mobileLimited.StatusCode);
        using var webLimited = await ChangeAsync(session.AccessToken, Password, Password, "/api/me/password");
        Assert.Equal(HttpStatusCode.TooManyRequests, webLimited.StatusCode);
    }

    [Fact]
    public async Task TheNewSessionKeepsActingForTheInstitutionThePhoneHadChosen()
    {
        var phone = await CreateUserAsync();
        await AssignRoleAsync(phone, database.AlphaInstitutionId);
        await AssignRoleAsync(phone, database.BetaInstitutionId);
        var session = await LogInAsync(phone);

        using var switched = await PostSessionAsync("switch-institution", new { refreshToken = session.Refresh, institution = "beta-temple" });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var acting = await ParseAsync(switched);

        using var changed = await ChangeAsync(acting.AccessToken, Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("beta-temple", (await ParseAsync(changed)).Body.GetProperty("activeInstitution").GetString());
    }
}

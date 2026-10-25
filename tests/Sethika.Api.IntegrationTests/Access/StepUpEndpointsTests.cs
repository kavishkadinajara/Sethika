using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// MFA step-up over HTTP (plan 5.3): a <c>RequiresMfa</c> permission is refused until the user
/// proves again that they hold their phone, against real SQL Server and Redis.
/// </summary>
/// <remarks>
/// The users here hold the test data's Alpha Steward role: <c>BOOKINGS.CALENDAR.VIEW</c>, and
/// <c>BOOKINGS.CALENDAR.APPROVE</c>, which these tests mark <c>RequiresMfa</c> while they run. No
/// booking permission needs a step-up in the catalogue (plan 03, E1); the mechanism is what is
/// tested here, so it is given one, and each approval is a real one, of the test data's BK-ALPHA.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed partial class StepUpEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "step-up-endpoint-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";
    private const string Approve = "/api/institution/bookings/BK-ALPHA/approve";
    private const string Calendar = "/api/institution/bookings?from=2026-01-01&to=2026-01-31";

    private readonly List<string> _phones = [];
    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;
    private IAsyncDisposable _approveNeedsStepUp = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _approveNeedsStepUp = await database.RequireStepUpAsync("BOOKINGS.CALENDAR.APPROVE");
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        _host = NewHost();
    }

    private WebApplicationFactory<Program> NewHost(Action<IServiceCollection>? replace = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms));
                services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter());
                replace?.Invoke(services);
            });
        });

    public async Task DisposeAsync()
    {
        await _approveNeedsStepUp.DisposeAsync();

        // The approvals name these users as their decider; the reseed clears them first.
        await database.SeedAsync();

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

        await _redis.DisposeAsync();
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

    /// <summary>Checks grants in real Redis but cannot store one, as when Redis drops mid-request.</summary>
    private sealed class GrantsThatCannotBeStored(IStepUpGrants real) : IStepUpGrants
    {
        public Task<bool> GrantAsync(Guid userId, string tokenId, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsGrantedAsync(Guid userId, string tokenId, CancellationToken cancellationToken = default) =>
            real.IsGrantedAsync(userId, tokenId, cancellationToken);
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

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigits();

    /// <summary>A signed-in session: the access token, what it names, and the cookies to refresh it.</summary>
    private sealed record Session(string AccessToken, Guid UserId, string TokenId, string Refresh, string Csrf);

    private HttpClient NewClient(string? accessToken = null, WebApplicationFactory<Program>? host = null)
    {
        var client = (host ?? _host).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    /// <summary>A user with a confirmed phone who holds Alpha Steward in Alpha.</summary>
    private async Task<string> CreateStewardAsync()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DECLARE @id UNIQUEIDENTIFIER = NEWID();

            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (@id, 'InstitutionStaff', N'Step-up Test', @phone, 1, @hash, 'en', 'Active');

            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.Code = 'ALPHA_STEWARD';
            """,
            new { phone, hash });

        return phone;
    }

    private async Task<Session> LogInAsync(string phone)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ToSession(response, await response.Content.ReadAsStringAsync());
    }

    private async Task<Session> RefreshAsync(Session session)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        request.Headers.Add("X-CSRF-Token", session.Csrf);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ToSession(response, await response.Content.ReadAsStringAsync());
    }

    private static Session ToSession(HttpResponseMessage response, string body)
    {
        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty("accessToken").GetString()!;
        var jwt = new JsonWebToken(accessToken);
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);

        return new Session(
            accessToken,
            Guid.Parse(jwt.GetClaim(SethikaClaimTypes.UserId).Value),
            jwt.Id,
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString());
    }

    private async Task<HttpResponseMessage> PostAsync(Session? session, string path, object? body = null)
    {
        using var client = NewClient(session?.AccessToken);
        return await client.PostAsync(path, body is null ? null : JsonContent.Create(body));
    }

    /// <summary>Asks for a step-up code and returns it as the phone received it.</summary>
    private async Task<string> RequestCodeAsync(Session session, string phone)
    {
        using var response = await PostAsync(session, "/api/auth/stepup/request");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return LastCodeSentTo(phone);
    }

    private string LastCodeSentTo(string phone) =>
        SixDigits().Match(_sms.Last(m => m.Phone == phone).Message).Value;

    private async Task StepUpAsync(Session session, string phone)
    {
        var code = await RequestCodeAsync(session, phone);

        using var response = await PostAsync(session, "/api/auth/stepup/verify", new { code });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task AssertStepUpRequiredAsync(Session session)
    {
        using var response = await PostAsync(session, Approve);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("stepup_required", await CodeOf(response));
    }

    /// <summary>Approves BK-ALPHA, put back to Requested first so that every call has something to approve.</summary>
    private async Task AssertApprovedAsync(Session session)
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;
                UPDATE booking.Bookings SET Status = 'Requested', DecidedBy = NULL, DecidedAt = NULL WHERE BookingNo = 'BK-ALPHA';
                """);
        }

        using var response = await PostAsync(session, Approve);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Approved", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    // ------------------------------------------------------------------ the grant

    [Fact]
    public async Task ARequiresMfaPermissionNeedsAStepUp_ThenWorks_UntilTheGrantExpires()
    {
        // Plan 5.8: 403 before step-up, 200 after, and 403 again once the TTL expires.
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);

        // VIEW is not RequiresMfa, and needs nothing more than the permission.
        using (var client = NewClient(session.AccessToken))
        using (var view = await client.GetAsync(Calendar))
        {
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        }

        await AssertStepUpRequiredAsync(session);

        await StepUpAsync(session, phone);
        await AssertApprovedAsync(session);

        // One step-up covers every sensitive action while it lasts, not just one.
        await AssertApprovedAsync(session);

        var key = RedisStepUpGrants.KeyFor(session.UserId, session.TokenId);
        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(14.5), TimeSpan.FromMinutes(15));

        await _redis.GetDatabase().KeyExpireAsync(key, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        await AssertStepUpRequiredAsync(session);
    }

    [Fact]
    public async Task AStepUpIsRecordedInLoginHistory()
    {
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);

        await StepUpAsync(session, phone);

        await using var connection = new SqlConnection(database.ConnectionString);
        var rows = (await connection.QueryAsync<(bool Succeeded, bool MfaUsed, string LoginName)>(
            "SELECT Succeeded, MfaUsed, LoginName FROM auth.LoginHistory WHERE UserId = @userId ORDER BY Id;",
            new { userId = session.UserId })).ToList();

        // The login, then the step-up.
        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].MfaUsed);
        Assert.Equal((true, true, phone), rows[1]);
    }

    [Fact]
    public async Task ARefreshedTokenDoesNotInheritTheStepUp()
    {
        // Plan A5: the grant belongs to the access token it was made for. Whoever refreshes a stolen
        // refresh token gets a token without it.
        var phone = await CreateStewardAsync();
        var first = await LogInAsync(phone);
        await StepUpAsync(first, phone);

        var second = await RefreshAsync(first);

        Assert.NotEqual(first.TokenId, second.TokenId);
        await AssertStepUpRequiredAsync(second);
        await AssertApprovedAsync(first);
    }

    // ------------------------------------------------------------------ the code

    [Fact]
    public async Task AWrongCodeGrantsNothing()
    {
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);
        var code = await RequestCodeAsync(session, phone);
        var wrong = code == "000000" ? "111111" : "000000";

        using var response = await PostAsync(session, "/api/auth/stepup/verify", new { code = wrong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("otp_incorrect", await CodeOf(response));
        await AssertStepUpRequiredAsync(session);
    }

    [Fact]
    public async Task ASignupCodeForTheSamePhoneIsNotAStepUpCode()
    {
        // Anyone can have a signup code sent to any number, including their victim's. If it also
        // satisfied step-up, step-up would prove nothing (OtpPurpose).
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);

        using (var anonymous = NewClient())
        using (var signup = await anonymous.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "signup" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
        }

        var signupCode = LastCodeSentTo(phone);
        var stepUpCode = await RequestCodeAsync(session, phone);
        Assert.NotEqual(signupCode, stepUpCode);

        using var response = await PostAsync(session, "/api/auth/stepup/verify", new { code = signupCode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertStepUpRequiredAsync(session);
    }

    [Fact]
    public async Task AGrantThatCouldNotBeStoredIsNotReportedAsDone()
    {
        // The code was right, but the grant never reached Redis. Saying 204 would send the client
        // back to a sensitive action that is then refused, with no way to tell why.
        await using var host = NewHost(services => services.AddSingleton<IStepUpGrants>(provider =>
            new GrantsThatCannotBeStored(ActivatorUtilities.CreateInstance<RedisStepUpGrants>(provider))));

        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);
        var code = await RequestCodeAsync(session, phone);

        using var client = NewClient(session.AccessToken, host);
        using var response = await client.PostAsync("/api/auth/stepup/verify", JsonContent.Create(new { code }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("verification_unavailable", await CodeOf(response));
        await AssertStepUpRequiredAsync(session);
    }

    // ------------------------------------------------------------------ who may step up

    [Fact]
    public async Task StepUpNeedsASignedInUser()
    {
        using var request = await PostAsync(null, "/api/auth/stepup/request");
        using var verify = await PostAsync(null, "/api/auth/stepup/verify", new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, request.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact]
    public async Task AnAccountWithoutAConfirmedPhoneCannotStepUp()
    {
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);
        await ExecuteAsync("UPDATE auth.Users SET PhoneConfirmed = 0 WHERE PhoneNumber = @phone;", new { phone });

        using var response = await PostAsync(session, "/api/auth/stepup/request");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_verified_phone", await CodeOf(response));
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Fact]
    public async Task ASuspendedAccountCannotStepUpOnItsStillValidToken()
    {
        // The access token outlives a suspension by up to fifteen minutes (plan A2). Step-up is
        // where that stops: nothing sensitive is reachable with it.
        var phone = await CreateStewardAsync();
        var session = await LogInAsync(phone);
        var code = await RequestCodeAsync(session, phone);

        await ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @phone;", new { phone });

        using var response = await PostAsync(session, "/api/auth/stepup/verify", new { code });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_not_active", await CodeOf(response));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(RedisStepUpGrants.KeyFor(session.UserId, session.TokenId)));
    }
}

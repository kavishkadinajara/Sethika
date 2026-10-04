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
/// The anonymous auth endpoints over HTTP, through the real pipeline: forwarded headers, rate-limit
/// filters, handlers, cookies - against real SQL Server and Redis.
/// </summary>
/// <remarks>
/// Client addresses are simulated the way production sees them: the connection comes from loopback,
/// which ASP.NET Core trusts as a proxy, and the client is named in X-Forwarded-For. Each test uses
/// random addresses and phone numbers, because rate-limit counters live in Redis for up to an hour
/// and would otherwise carry over between tests and between runs.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class AuthEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "auth-endpoint-test-signing-key-at-least-32-bytes!!";
    private const string Password = "Dana!2026Sethika";

    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private readonly List<string> _phones = [];
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public Task InitializeAsync() => database.SeedAsync();

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

                DELETE FROM core.OutboxMessages WHERE AggregateType = 'User'
                    AND AggregateId IN (SELECT LOWER(CONVERT(VARCHAR(36), Id)) FROM @ids);
                DELETE FROM auth.UserConsents  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.DonorProfiles WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
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

    /// <summary>Gives every request the connection address a real socket would have.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> Host(IPAddress? connectionAddress = null, string? redis = null)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", redis ?? database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms));
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(connectionAddress ?? IPAddress.Loopback));
            });
        });

        _hosts.Add(host);
        return host;
    }

    /// <summary>A client whose requests arrive through the trusted proxy, forwarded for <paramref name="clientAddress"/>.</summary>
    private static HttpClient Client(WebApplicationFactory<Program> host, string? clientAddress = null)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress ?? NewClientAddress());
        return client;
    }

    private static string NewClientAddress() =>
        $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}";

    private string NewPhone()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private static string LocalSpelling(string e164) => "0" + e164[3..];

    private async Task<string> CreateUserAsync(WebApplicationFactory<Program> host, string status = "Active")
    {
        var phone = NewPhone();
        var hash = host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), 'InstitutionStaff', N'Endpoint Test', @phone, 1, @hash, 'en', @status);
            """,
            new { phone, hash, status });

        return phone;
    }

    private static Task<HttpResponseMessage> LogInAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { login, password });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue.ParseStrictList([.. values])
            : [];

    // ------------------------------------------------------------------ end to end

    [Fact]
    public async Task ADonorSignsUpWithAPhoneAndMakesAnAuthenticatedCall()
    {
        // Plan 5.8, first box: signup -> OTP -> login -> authenticated call, over HTTP only.
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        var documents = await client.GetFromJsonAsync<JsonElement[]>("/api/legal/current?language=si");
        Assert.NotNull(documents);
        Assert.Equal(2, documents.Length);
        Assert.All(documents, d => Assert.Equal("si", d.GetProperty("language").GetString()));

        using var request = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = LocalSpelling(phone), purpose = "signup", language = "si" });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);

        var code = new string([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);
        using var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = LocalSpelling(phone), code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var ticket = (await JsonAsync(verify)).GetProperty("ticket").GetString();

        using var signUp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            phoneNumber = LocalSpelling(phone),
            verificationTicket = ticket,
            fullName = "Dana Donor",
            password = Password,
            preferredLanguage = "si",
            acceptedDocumentHashes = documents.Select(d => d.GetProperty("contentSha256").GetString()),
        });
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        Assert.Contains(SetCookies(signUp), c => c.Name == "sethika-refresh");

        var accessToken = (await JsonAsync(signUp)).GetProperty("accessToken").GetString();

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me/permissions");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        using var login = await LogInAsync(client, phone, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ------------------------------------------------------------------ login

    [Fact]
    public async Task UnknownLoginWrongPasswordAndLockedAccountGetTheSameAnswer()
    {
        // Plan 5.8: indistinguishable in status and body (timing is proven at the handler). The
        // locked account is sent its correct password - the case where a distinct answer would
        // confirm the account to whoever caused the lock.
        var host = Host();
        using var client = Client(host);
        var user = await CreateUserAsync(host);
        var locked = await CreateUserAsync(host);

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE auth.Users SET LockoutEnd = DATEADD(MINUTE, 10, SYSDATETIMEOFFSET()) WHERE PhoneNumber = @locked;",
                new { locked });
        }

        using var unknown = await LogInAsync(client, NewPhone(), Password);
        using var wrong = await LogInAsync(client, user, "Wrong!2026Password");
        using var lockedOut = await LogInAsync(client, locked, Password);

        var answers = new[] { unknown, wrong, lockedOut };
        Assert.All(answers, a => Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode));
        Assert.All(answers, a => Assert.Empty(SetCookies(a)));

        var bodies = await Task.WhenAll(answers.Select(a => a.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Single(answers.Select(a => a.Content.Headers.ContentType?.ToString()).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASuccessfulLoginSetsTheCookiesPlanA2Describes()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(host);

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
                VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'), 'ENDPOINT_TEST', N'Endpoint test', 0);
                INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
                SELECT u.Id, CAST(SCOPE_IDENTITY() AS INT), @institutionId, 1 FROM auth.Users u WHERE u.PhoneNumber = @phone;
                """,
                new { phone, institutionId = database.AlphaInstitutionId });
        }

        using var response = await LogInAsync(client, LocalSpelling(phone), Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var cookies = SetCookies(response);
        var refresh = Assert.Single(cookies, c => c.Name == "sethika-refresh");
        var csrf = Assert.Single(cookies, c => c.Name == "sethika-csrf");

        // Script can never read the refresh token; it is sent only to the one path that uses it.
        Assert.True(refresh.HttpOnly);
        Assert.Equal("/api/auth/session", refresh.Path.ToString());
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, refresh.SameSite);

        // Script must read the CSRF token, to echo it in a header on refresh.
        Assert.False(csrf.HttpOnly);
        Assert.Equal("/", csrf.Path.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(refresh.Value.ToString(), body, StringComparison.Ordinal);   // cookie only
        Assert.DoesNotContain("\"id\"", body, StringComparison.OrdinalIgnoreCase);        // slugs, never internal ids

        var json = await JsonAsync(response);
        Assert.Equal("alpha-temple", json.GetProperty("activeInstitution").GetString());

        // The cookie holds the token whose hash was stored - not the hash, and not some other token.
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refresh.Value.ToString())));
        await using var check = new SqlConnection(database.ConnectionString);
        Assert.Equal(1, await check.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.RefreshTokens WHERE TokenHash = @hash;", new { hash }));
    }

    [Fact]
    public async Task ASuspendedAccountWithTheRightPasswordIsForbidden()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(host, status: "Suspended");

        using var response = await LogInAsync(client, phone, Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_not_active", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task LoginIsLimitedPerLoginName_WhateverSpellingOfTheNumberIsUsed()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(host);

        for (var i = 0; i < 10; i++)
        {
            using var attempt = await LogInAsync(client, phone, "Wrong!2026Password");
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        // The eleventh, typed the local way. Counting spellings apart would give an attacker a fresh
        // budget per spelling of the same number.
        using var refused = await LogInAsync(client, LocalSpelling(phone), Password);
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);
        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 15 * 60);

        // Another login from the same client is not caught by this one's limit.
        using var other = await LogInAsync(client, NewPhone(), Password);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task PasswordLoginStillWorksWhenRedisIsDown()
    {
        // Plan A4's accepted consequence, and 5.4's fail-open rule: with Redis gone, codes cannot be
        // issued, but sign-in - which needs only SQL - carries on, and the rate limits let it through.
        var host = Host(redis: "127.0.0.1:1,connectTimeout=200,asyncTimeout=500,syncTimeout=500");
        using var client = Client(host);
        var phone = await CreateUserAsync(host);

        using var login = await LogInAsync(client, phone, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var otp = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, otp.StatusCode);
    }

    // ------------------------------------------------------------------ one-time codes

    [Fact]
    public async Task CodeRequestsAreLimitedPerClientAddress()
    {
        var host = Host();
        var address = NewClientAddress();
        using var client = Client(host, address);

        for (var i = 0; i < 20; i++)
        {
            using var sent = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        using var refused = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);

        using var elsewhere = Client(host);
        using var fromElsewhere = await elsewhere.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
        Assert.Equal(HttpStatusCode.Accepted, fromElsewhere.StatusCode);
    }

    [Fact]
    public async Task AClientOutsideTheTrustedProxiesCannotPickItsBucketWithXForwardedFor()
    {
        // The connection comes straight from a public address, not through a trusted proxy, so the
        // X-Forwarded-For it sends - a new one on every request - must be ignored. Otherwise the
        // per-client limit is a limit on nobody.
        var direct = IPAddress.Parse(
            $"100.{64 + RandomNumberGenerator.GetInt32(64)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        var host = Host(connectionAddress: direct);

        for (var i = 0; i < 20; i++)
        {
            using var spoofing = Client(host);   // a fresh, random X-Forwarded-For each time
            using var sent = await spoofing.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        using var last = Client(host);
        using var refused = await last.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = NewPhone() });
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);
    }

    [Fact]
    public async Task ASecondCodeTooSoonIsRefusedWithRetryAfter()
    {
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        using var first = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone });
        using var second = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
        Assert.InRange(int.Parse(Assert.Single(second.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture), 1, 60);
    }

    [Fact]
    public async Task AWrongCodeSaysHowManyAttemptsAreLeft()
    {
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        using var sent = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone });
        var code = new string([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);
        var wrongCode = code == "000000" ? "111111" : "000000";

        using var wrong = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, code = wrongCode });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var body = await JsonAsync(wrong);
        Assert.Equal("otp_incorrect", body.GetProperty("code").GetString());
        Assert.Equal(4, body.GetProperty("attemptsRemaining").GetInt32());

        using var right = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, code });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.False(string.IsNullOrEmpty((await JsonAsync(right)).GetProperty("ticket").GetString()));
    }

    [Theory]
    [InlineData("stepup")]
    [InlineData("login")]
    public async Task OnlySignupAndResetCodesCanBeRequestedAnonymously(string purpose)
    {
        // A step-up code sent to any number on request would be an SMS-bombing tool and a way around
        // the authenticated step-up flow. Reset is the other anonymous purpose (PasswordResetEndpointsTests).
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        using var response = await client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Fact]
    public async Task LegalDocumentsRefuseALanguageSethikaDoesNotSpeak()
    {
        var host = Host();
        using var client = Client(host);

        using var response = await client.GetAsync("/api/legal/current?language=fr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

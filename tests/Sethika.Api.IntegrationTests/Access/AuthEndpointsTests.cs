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
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

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

        // The one-time-code keys of every number this test used, so a number drawn again by a later
        // run starts with no code, no send count and no verify budget spent. Its own connection, not a
        // host's: one test points its host at a Redis that is not there.
        if (_phones.Count > 0)
        {
            await using var connection = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
            await connection.GetDatabase().KeyDeleteAsync([.. _phones.SelectMany(phone => new RedisKey[]
            {
                OtpCacheKey.ForCode(phone, OtpPurpose.Signup),
                OtpCacheKey.ForSendCount(phone, OtpPurpose.Signup),
                OtpCacheKey.ForTicket(phone, OtpPurpose.Signup),
                RateLimitKey.For(AuthRateLimitsOtpVerifyPerPhone, phone),
            })]);
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>The rule name of <c>AuthRateLimits.OtpVerifyPerPhone</c>, which is internal to the Api.</summary>
    private const string AuthRateLimitsOtpVerifyPerPhone = "otp-verify-phone";

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

    private string LastCodeSentTo(string phone) =>
        new([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);

    private static string AnotherCode(string code) => code == "000000" ? "111111" : "000000";

    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string phone) =>
        client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "signup" });

    private static Task<HttpResponseMessage> VerifyCodeAsync(HttpClient client, string phone, string code) =>
        client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, purpose = "signup", code });

    private static OtpOptions OtpOptionsOf(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IOptions<OtpOptions>>().Value;

    /// <summary>
    /// Moves the outstanding code to the age at which its resend cooldown has just run out.
    /// </summary>
    /// <remarks>
    /// The cooldown is read from the code's own TTL (see OtpService), so shortening the TTL is exactly
    /// what a minute of waiting does to it - and only to it: the hourly send counter is a separate key
    /// and keeps its window. The configured cooldown stays in force; the test just does not sleep.
    /// </remarks>
    private static async Task LetTheResendCooldownPassAsync(WebApplicationFactory<Program> host, string phone)
    {
        var options = OtpOptionsOf(host);
        var redis = host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var codeKey = OtpCacheKey.ForCode(phone, OtpPurpose.Signup);

        Assert.True(await redis.KeyExistsAsync(codeKey), "there is no outstanding code to age");
        Assert.True(await redis.KeyExpireAsync(codeKey, options.Lifetime - options.ResendCooldown - TimeSpan.FromSeconds(1)));
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

    [Fact]
    public async Task FiveWrongCodesBurnTheCode_SoTheRightCodeIsRefusedAfterThem()
    {
        // Plan 5.8 and A4: five verify attempts per code, then the code is burned. The fifth wrong
        // guess is answered as exhausted rather than "incorrect, 0 left", and from then on the code
        // does not exist - the right code included, which is what makes the cap a cap.
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        using var sent = await RequestCodeAsync(client, phone);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var code = LastCodeSentTo(phone);

        var remaining = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            using var wrong = await VerifyCodeAsync(client, phone, AnotherCode(code));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            var body = await JsonAsync(wrong);
            Assert.Equal("otp_incorrect", body.GetProperty("code").GetString());
            remaining.Add(body.GetProperty("attemptsRemaining").GetInt32());
        }

        Assert.Equal([4, 3, 2, 1], remaining);

        using var fifth = await VerifyCodeAsync(client, phone, AnotherCode(code));
        Assert.Equal(HttpStatusCode.BadRequest, fifth.StatusCode);
        var exhausted = await JsonAsync(fifth);
        Assert.Equal("otp_attempts_exhausted", exhausted.GetProperty("code").GetString());
        Assert.False(exhausted.TryGetProperty("attemptsRemaining", out _));

        using var right = await VerifyCodeAsync(client, phone, code);
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);
        var refused = await JsonAsync(right);
        Assert.Equal("otp_not_found", refused.GetProperty("code").GetString());
        Assert.False(refused.TryGetProperty("ticket", out _));
    }

    [Fact]
    public async Task AfterACodeIsBurnedAFreshRequestIssuesANewCodeThatWorks()
    {
        // The resend cooldown is measured on the outstanding code (A4: 60 s), and burning deletes it,
        // so a new code may be requested at once without waiting out a cooldown. It is the second of
        // the number's sends this hour, inside the cap, so nothing here is rate-limited.
        var host = Host();
        using var client = Client(host);
        var phone = NewPhone();

        using var first = await RequestCodeAsync(client, phone);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var burned = LastCodeSentTo(phone);

        for (var i = 0; i < OtpOptionsOf(host).MaxVerifyAttempts; i++)
        {
            using var wrong = await VerifyCodeAsync(client, phone, AnotherCode(burned));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using var again = await RequestCodeAsync(client, phone);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(2, _sms.Count(m => m.Phone == phone));

        var fresh = LastCodeSentTo(phone);
        using var verified = await VerifyCodeAsync(client, phone, fresh);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.False(string.IsNullOrEmpty((await JsonAsync(verified)).GetProperty("ticket").GetString()));
    }

    [Fact]
    public async Task ANumberGetsItsHourlySendsAndTheNextIsRefusedWithRetryAfter_WhileAnotherNumberIsNot()
    {
        // A4's cost control: three codes per number per hour. Each send comes from a different client
        // address, so what refuses the fourth is the per-number cap and not the per-client limit - and
        // the other number is asked for from the refused client, so the cap is not per client either.
        var host = Host();
        var options = OtpOptionsOf(host);
        Assert.InRange(options.MaxSendsPerWindow, 1, 10);   // keeps the loop below honest about its size

        var phone = NewPhone();

        for (var i = 0; i < options.MaxSendsPerWindow; i++)
        {
            if (i > 0)
            {
                await LetTheResendCooldownPassAsync(host, phone);
            }

            using var sender = Client(host);
            using var sent = await RequestCodeAsync(sender, phone);
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        await LetTheResendCooldownPassAsync(host, phone);

        using var client = Client(host);
        using var refused = await RequestCodeAsync(client, phone);
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);

        // The window's remainder, not the cooldown's: longer than a cooldown, at most the window.
        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, (int)options.ResendCooldown.TotalSeconds + 1, (int)options.SendWindow.TotalSeconds);
        Assert.Equal(options.MaxSendsPerWindow, _sms.Count(m => m.Phone == phone));

        var other = NewPhone();
        using var otherSent = await RequestCodeAsync(client, other);
        Assert.Equal(HttpStatusCode.Accepted, otherSent.StatusCode);
        Assert.Single(_sms, m => m.Phone == other);
    }

    [Theory]
    [InlineData("stepup")]
    public async Task OnlySignupResetAndSignInCodesCanBeRequestedAnonymously(string purpose)
    {
        // A step-up code sent to any number on request would be an SMS-bombing tool and a way around
        // the authenticated step-up flow. Reset and sign-in are the other anonymous purposes
        // (PasswordResetEndpointsTests, OtpSignInEndpointsTests).
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

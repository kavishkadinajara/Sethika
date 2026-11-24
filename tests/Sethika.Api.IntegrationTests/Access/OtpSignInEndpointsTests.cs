using System.Collections.Concurrent;
using System.Diagnostics;
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Sign-in by SMS code over HTTP, against real SQL Server and Redis: <c>/api/auth/otp/request</c>
/// with purpose <c>login</c>, then <c>/api/auth/otp/sign-in</c>.
/// </summary>
/// <remarks>
/// Most of these compare a registered number with one that has no account, because that is what the
/// flow has to hide: anyone can type any number. Phone numbers and client addresses are random per
/// test, because cooldowns and rate-limit counters live in Redis for up to an hour.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class OtpSignInEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "otp-sign-in-test-signing-key-at-least-32-bytes!!";

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

                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids) OR LoginName IN @phones;
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                DELETE FROM auth.Roles         WHERE Code = 'OTP_SIGNIN_TEST';
                """,
                new { phones = _phones });
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Records what would have been texted. <paramref name="latency"/> stands in for a gateway's round trip.</summary>
    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent, TimeSpan latency) : ISmsSender
    {
        public async Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            if (latency > TimeSpan.Zero)
            {
                await Task.Delay(latency, cancellationToken);
            }

            sent.Enqueue((phoneNumber, message));
        }
    }

    private sealed class RemoteAddressStartupFilter : IStartupFilter
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

    /// <param name="floor">The undisclosed-send floor. Zero unless a test is timing it, so the suite stays fast.</param>
    /// <param name="cooldown">The resend cooldown; zero lets a test reach the hourly cap.</param>
    /// <param name="smsLatency">How long each SMS takes to send.</param>
    private WebApplicationFactory<Program> Host(TimeSpan? floor = null, TimeSpan? cooldown = null, TimeSpan? smsLatency = null)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Otp:UndisclosedSendDuration", (floor ?? TimeSpan.Zero).ToString("c", CultureInfo.InvariantCulture));

            if (cooldown is { } resend)
            {
                builder.UseSetting("Otp:ResendCooldown", resend.ToString("c", CultureInfo.InvariantCulture));
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms, smsLatency ?? TimeSpan.Zero));
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter());
            });
        });

        _hosts.Add(host);
        return host;
    }

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
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private static string LocalSpelling(string e164) => "0" + e164[3..];

    private async Task<string> CreateUserAsync(
        string status = "Active",
        bool phoneConfirmed = true,
        string language = "en",
        int accessFailedCount = 0,
        bool mfaRequired = false)
    {
        var phone = NewPhone();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, AccessFailedCount, MfaRequired)
            VALUES (NEWID(), 'Donor', N'Otp Sign-in Test', @phone, @phoneConfirmed, @language, @status, @accessFailedCount, @mfaRequired);
            """,
            new { phone, phoneConfirmed, language, status, accessFailedCount, mfaRequired });

        return phone;
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

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string phoneNumber, string purpose = "login") =>
        client.PostAsJsonAsync("/api/auth/otp/request", new { phoneNumber, purpose });

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string phoneNumber, string code) =>
        client.PostAsJsonAsync("/api/auth/otp/sign-in", new { phoneNumber, code });

    private string CodeSentTo(string phone) =>
        new([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsAsciiDigit)]);

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync());

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue.ParseStrictList([.. values])
            : [];

    /// <summary>Status, content type and body: everything a caller can compare between two answers.</summary>
    private static async Task<string> AnswerAsync(HttpResponseMessage response) =>
        $"{(int)response.StatusCode} {response.Content.Headers.ContentType} {await response.Content.ReadAsStringAsync()} cookies={SetCookies(response).Count}";

    // ------------------------------------------------------------------ signing in

    [Fact]
    public async Task ARegisteredNumberSignsInWithACode_AndGetsWhatPasswordLoginGives()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(language: "si");

        await ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'), 'OTP_SIGNIN_TEST', N'Otp sign-in test', 0);
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT u.Id, CAST(SCOPE_IDENTITY() AS INT), @institutionId, 1 FROM auth.Users u WHERE u.PhoneNumber = @phone;
            """,
            new { phone, institutionId = database.AlphaInstitutionId });

        using var requested = await RequestAsync(client, LocalSpelling(phone));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        // In the account's language, and saying what the code is for.
        var message = _sms.Single(m => m.Phone == phone).Message;
        Assert.Contains("පිවිසුම්", message, StringComparison.Ordinal);

        using var response = await SignInAsync(client, LocalSpelling(phone), CodeSentTo(phone));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var cookies = SetCookies(response);
        var refresh = Assert.Single(cookies, c => c.Name == "sethika-refresh");
        Assert.True(refresh.HttpOnly);
        Assert.Equal("/api/auth/session", refresh.Path.ToString());
        Assert.Single(cookies, c => c.Name == "sethika-csrf");

        // The body password login returns, property for property, with the institution rule applied.
        var json = await JsonAsync(response);
        Assert.Equal(
            ["accessToken", "accessTokenExpiresAt", "institutions", "activeInstitution"],
            json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("alpha-temple", json.GetProperty("activeInstitution").GetString());

        // The refresh family is bound to the account's current security stamp, so logout-all ends it.
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refresh.Value.ToString())));
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RefreshTokens t JOIN auth.Users u ON u.Id = t.UserId
            WHERE t.TokenHash = @hash AND t.SecurityStamp = u.SecurityStamp AND u.PhoneNumber = @phone;
            """,
            new { hash, phone }));

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me/permissions");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        using var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.LoginHistory h JOIN auth.Users u ON u.Id = h.UserId
            WHERE u.PhoneNumber = @phone AND h.Succeeded = 1 AND h.FailureReason IS NULL;
            """,
            new { phone }));
    }

    [Fact]
    public async Task AnUnknownNumberGetsTheSameAnswerToTheRequest_IsSentNothing_AndCannotSignIn()
    {
        var host = Host();
        using var client = Client(host);
        var registered = await CreateUserAsync();
        var unknown = NewPhone();

        using var toRegistered = await RequestAsync(client, registered);
        using var toUnknown = await RequestAsync(client, unknown);

        Assert.Equal(HttpStatusCode.Accepted, toRegistered.StatusCode);
        Assert.Equal(await AnswerAsync(toRegistered), await AnswerAsync(toUnknown));
        Assert.Single(_sms, m => m.Phone == registered);
        Assert.DoesNotContain(_sms, m => m.Phone == unknown);

        // A wrong code for the registered number and any code for the unknown one: one answer.
        var code = CodeSentTo(registered);
        using var wrong = await SignInAsync(client, registered, WrongCode(code));
        using var unknownWithCode = await SignInAsync(client, unknown, code);
        using var unknownWithOther = await SignInAsync(client, unknown, WrongCode(code));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("otp_invalid", (await JsonAsync(wrong)).GetProperty("code").GetString());
        Assert.Equal(await AnswerAsync(wrong), await AnswerAsync(unknownWithCode));
        Assert.Equal(await AnswerAsync(wrong), await AnswerAsync(unknownWithOther));
    }

    [Fact]
    public async Task ARequestTakesAsLongForAnUnknownNumberAsForOneThatIsTexted()
    {
        // The SMS is the only step a registered number pays for. With a gateway taking 250 ms and a
        // floor of 600 ms, both answers should take the floor. Without the floor, the unknown number
        // answers in a few milliseconds - the gap this guards. The bound is loose on purpose: it is
        // there to catch a skipped floor, not to measure jitter.
        //
        // Fastest of three per side, as SignInHandlerTests does: noise only ever adds time.
        var floor = TimeSpan.FromMilliseconds(600);
        var host = Host(floor: floor, smsLatency: TimeSpan.FromMilliseconds(250));
        using var client = Client(host);

        using (await RequestAsync(client, NewPhone()))
        {
            // warm-up: JIT, connection pools
        }

        async Task<double> FastestMillisecondsAsync(Func<Task<string>> phoneOf)
        {
            var fastest = double.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var phone = await phoneOf();
                var started = Stopwatch.GetTimestamp();
                using var response = await RequestAsync(client, phone);
                fastest = Math.Min(fastest, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }

            return fastest;
        }

        var registered = await FastestMillisecondsAsync(() => CreateUserAsync());
        var unknown = await FastestMillisecondsAsync(() => Task.FromResult(NewPhone()));

        Assert.True(unknown >= floor.TotalMilliseconds * 0.95, $"unknown number answered in {unknown:F0} ms, under the {floor.TotalMilliseconds:F0} ms floor");
        Assert.True(Math.Abs(registered - unknown) < 150, $"registered {registered:F0} ms vs unknown {unknown:F0} ms");
    }

    [Fact]
    public async Task AWrongCodeFiveTimesBurnsIt()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using (await RequestAsync(client, phone))
        {
        }

        var code = CodeSentTo(phone);
        var answers = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            using var wrong = await SignInAsync(client, phone, WrongCode(code));
            answers.Add(await AnswerAsync(wrong));
        }

        // The right code, now burned - answered like every wrong one, attempts left told to nobody.
        using var burned = await SignInAsync(client, phone, code);
        answers.Add(await AnswerAsync(burned));

        Assert.Equal(HttpStatusCode.Unauthorized, burned.StatusCode);
        Assert.Single(answers.Distinct(StringComparer.Ordinal));

        // A wrong code is not a wrong password: the lockout count is untouched.
        Assert.Equal(0, await ScalarAsync<int>("SELECT AccessFailedCount FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }));
    }

    [Fact]
    public async Task ACodeWorksOnlyOnce()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using (await RequestAsync(client, phone))
        {
        }

        var code = CodeSentTo(phone);

        using var first = await SignInAsync(client, phone, code);
        using var again = await SignInAsync(client, phone, code);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Empty(SetCookies(again));
    }

    [Fact]
    public async Task ASignupCodeCannotSignIn()
    {
        // Anyone can have a signup code sent to a registered number - signup does not refuse them -
        // so a signup code that signed in would be a second way to the same account with no limit of
        // its own. The purpose in the key is what stops it.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using var requested = await RequestAsync(client, phone, purpose: "signup");
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        using var response = await SignInAsync(client, phone, CodeSentTo(phone));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(SetCookies(response));
    }

    [Fact]
    public async Task ASignInCodeCannotBeUsedForSignup_NorSpentThroughVerify()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using (await RequestAsync(client, phone))
        {
        }

        var code = CodeSentTo(phone);

        // As a signup code: there is none outstanding for this number.
        using var asSignup = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, purpose = "signup", code });
        Assert.Equal(HttpStatusCode.BadRequest, asSignup.StatusCode);
        Assert.Equal("otp_not_found", (await JsonAsync(asSignup)).GetProperty("code").GetString());

        // Verify does not take sign-in codes at all - it would tell attempts left, which sign-in hides.
        using var asLogin = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phoneNumber = phone, purpose = "login", code });
        Assert.Equal(HttpStatusCode.BadRequest, asLogin.StatusCode);
        Assert.Equal("unsupported_purpose", (await JsonAsync(asLogin)).GetProperty("code").GetString());

        // Neither attempt spent it.
        using var signedIn = await SignInAsync(client, phone, code);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task AFourthRequestWithinTheHourGets429_ForAnUnknownNumberAsForARegisteredOne()
    {
        var host = Host(cooldown: TimeSpan.Zero);
        using var client = Client(host);
        var registered = await CreateUserAsync();
        var unknown = NewPhone();

        foreach (var phone in new[] { registered, unknown })
        {
            for (var i = 0; i < 3; i++)
            {
                using var sent = await RequestAsync(client, phone);
                Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
            }
        }

        using var fourthRegistered = await RequestAsync(client, registered);
        using var fourthUnknown = await RequestAsync(client, unknown);

        Assert.Equal((HttpStatusCode)429, fourthRegistered.StatusCode);
        Assert.Equal(await AnswerAsync(fourthRegistered), await AnswerAsync(fourthUnknown));
        Assert.InRange(int.Parse(Assert.Single(fourthUnknown.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture), 1, 3600);
        Assert.Equal(3, _sms.Count(m => m.Phone == registered));
    }

    [Fact]
    public async Task SignInAndSignupCodeRequestsSpendSeparateBudgetsPerClient()
    {
        var host = Host();
        var address = NewClientAddress();
        using var client = Client(host, address);

        for (var i = 0; i < 20; i++)
        {
            using var sent = await RequestAsync(client, NewPhone(), purpose: "signup");
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        using var signupRefused = await RequestAsync(client, NewPhone(), purpose: "signup");
        Assert.Equal((HttpStatusCode)429, signupRefused.StatusCode);

        for (var i = 0; i < 20; i++)
        {
            using var sent = await RequestAsync(client, NewPhone());
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        using var loginRefused = await RequestAsync(client, NewPhone());
        Assert.Equal((HttpStatusCode)429, loginRefused.StatusCode);
    }

    // ------------------------------------------------------------------ who may sign in

    [Fact]
    public async Task AnAccountThePlatformMadeIsSentACode_AndItsFirstSignInConfirmsTheNumber()
    {
        // Plan 04, Y6: made by an administrator for someone who cannot sign up - pending, unconfirmed,
        // no password. The code is the only way in, and the first right one proves the phone.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(status: "PendingVerification", phoneConfirmed: false);

        using var requested = await RequestAsync(client, phone);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        using var response = await SignInAsync(client, phone, CodeSentTo(phone));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active|1", await ScalarAsync<string>(
            "SELECT CONCAT(Status, '|', CAST(PhoneConfirmed AS INT)) FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }));
    }

    [Fact]
    public async Task APendingAccountWithAPasswordIsSentNoCode()
    {
        // Someone who set a password but never confirmed the number did not come from the platform:
        // the rule for an unconfirmed number holds for them.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(status: "PendingVerification", phoneConfirmed: false);
        await ExecuteAsync("UPDATE auth.Users SET PasswordHash = N'not-a-real-hash' WHERE PhoneNumber = @phone;", new { phone });

        using var response = await RequestAsync(client, phone);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Theory]
    [InlineData("Suspended", true)]
    [InlineData("Active", false)]
    public async Task AnAccountThatMayNotSignInIsSentNoCode(string status, bool phoneConfirmed)
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(status: status, phoneConfirmed: phoneConfirmed);
        var unknown = NewPhone();

        using var response = await RequestAsync(client, phone);
        using var toUnknown = await RequestAsync(client, unknown);

        Assert.Equal(await AnswerAsync(toUnknown), await AnswerAsync(response));
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Fact]
    public async Task AnMfaRequiredAccountIsSentNoCode_AndIsAnsweredLikeANumberWithNoAccount()
    {
        // Platform staff and institution owners. A code alone is one factor, and the step-up code
        // goes to the same phone, so SMS sign-in would make the SIM the whole account.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(mfaRequired: true);
        var unknown = NewPhone();

        using var response = await RequestAsync(client, phone);
        using var toUnknown = await RequestAsync(client, unknown);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(await AnswerAsync(toUnknown), await AnswerAsync(response));
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Fact]
    public async Task ACodeHeldByAnMfaRequiredAccountIsRefusedLikeAWrongOne()
    {
        // The code was sent before the account became MfaRequired; the right code still opens nothing.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using (await RequestAsync(client, phone))
        {
        }

        var code = CodeSentTo(phone);
        using var wrong = await SignInAsync(client, phone, WrongCode(code));

        await ExecuteAsync("UPDATE auth.Users SET MfaRequired = 1 WHERE PhoneNumber = @phone;", new { phone });

        using var refused = await SignInAsync(client, phone, code);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(await AnswerAsync(wrong), await AnswerAsync(refused));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.RefreshTokens t JOIN auth.Users u ON u.Id = t.UserId WHERE u.PhoneNumber = @phone;",
            new { phone }));
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.LoginHistory h JOIN auth.Users u ON u.Id = h.UserId
            WHERE u.PhoneNumber = @phone AND h.Succeeded = 0 AND h.FailureReason = 'MfaRequired';
            """,
            new { phone }));
    }

    [Fact]
    public async Task ALockedAccountIsSentNoCode_AndACodeFromBeforeTheLockIsRefusedLikeAWrongOne()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();
        var locked = await CreateUserAsync();

        await ExecuteAsync(
            "UPDATE auth.Users SET LockoutEnd = DATEADD(MINUTE, 10, SYSDATETIMEOFFSET()) WHERE PhoneNumber = @locked;",
            new { locked });

        using (await RequestAsync(client, locked))
        {
        }

        Assert.DoesNotContain(_sms, m => m.Phone == locked);

        using (await RequestAsync(client, phone))
        {
        }

        var code = CodeSentTo(phone);
        using var wrong = await SignInAsync(client, phone, WrongCode(code));

        await ExecuteAsync(
            "UPDATE auth.Users SET LockoutEnd = DATEADD(MINUTE, 10, SYSDATETIMEOFFSET()) WHERE PhoneNumber = @phone;",
            new { phone });

        using var refused = await SignInAsync(client, phone, code);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(await AnswerAsync(wrong), await AnswerAsync(refused));
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.LoginHistory h JOIN auth.Users u ON u.Id = h.UserId
            WHERE u.PhoneNumber = @phone AND h.FailureReason = 'LockedOut';
            """,
            new { phone }));
    }

    [Fact]
    public async Task AnAccountSuspendedAfterItsCodeWasSentIsToldSo_AsPasswordLoginTellsIt()
    {
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync();

        using (await RequestAsync(client, phone))
        {
        }

        await ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @phone;", new { phone });

        using var response = await SignInAsync(client, phone, CodeSentTo(phone));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_not_active", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.Empty(SetCookies(response));
    }

    [Fact]
    public async Task ASuccessfulCodeSignInLeavesThePasswordFailureCountAlone()
    {
        // Plan A1's reset belongs to the password. Clearing it here would hand someone guessing the
        // password a fresh five guesses every time the owner signs in by code.
        var host = Host();
        using var client = Client(host);
        var phone = await CreateUserAsync(accessFailedCount: 3);

        using (await RequestAsync(client, phone))
        {
        }

        using var response = await SignInAsync(client, phone, CodeSentTo(phone));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, await ScalarAsync<int>("SELECT AccessFailedCount FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM auth.Users WHERE PhoneNumber = @phone AND LastLoginAt IS NOT NULL;", new { phone }));
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
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
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.PasswordReset;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// The forgotten-password reset over HTTP (plan 5.10): an SMS code to the account's own phone, a
/// ticket for the verified code, a new password with the ticket - against real SQL Server and Redis.
/// </summary>
/// <remarks>
/// Every number and client address is random, because the OTP and rate-limit counters live in Redis
/// for up to an hour and would otherwise carry over between tests and runs. Each reset request takes
/// at least <see cref="PasswordResetHandler.RequestFloor"/>, which is what makes this class slow.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed partial class PasswordResetEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "password-reset-test-signing-key-at-least-32-bytes!!";
    private const string OldPassword = "Dana!2026Sethika";
    private const string NewPassword = "Lotus#2027Offering";

    private readonly List<string> _phones = [];
    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
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
                    AND AggregateId IN (SELECT LOWER(CONVERT(VARCHAR(36), Id)) FROM @ids);
                UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
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

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        return client;
    }

    private string NewPhone()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private async Task<string> CreateUserAsync(
        bool mfaRequired = false,
        string status = "Active",
        bool isDeleted = false,
        string language = "en")
    {
        var phone = NewPhone();
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), OldPassword);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, MfaRequired,
                               PreferredLanguage, Status, IsDeleted, DeletedAt)
            VALUES (NEWID(), CASE WHEN @mfaRequired = 1 THEN 'PlatformStaff' ELSE 'Donor' END, N'Reset Test', @phone, 1,
                    @hash, @mfaRequired, @language, @status, @isDeleted,
                    CASE WHEN @isDeleted = 1 THEN SYSUTCDATETIME() END);
            """,
            new { phone, hash, mfaRequired, status, isDeleted, language });

        return phone;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private Task<Guid> UserIdOf(string phone) =>
        QuerySingleAsync<Guid>("SELECT Id FROM auth.Users WHERE PhoneNumber = @phone;", new { phone });

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using var client = NewClient();
        return await client.PostAsJsonAsync(path, body);
    }

    private Task<HttpResponseMessage> RequestResetAsync(string phone, string? language = null) =>
        PostAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "reset", language });

    private Task<HttpResponseMessage> VerifyAsync(string phone, string code, string purpose = "reset") =>
        PostAsync("/api/auth/otp/verify", new { phoneNumber = phone, purpose, code });

    private Task<HttpResponseMessage> ResetAsync(string? ticket, string newPassword = NewPassword) =>
        PostAsync("/api/auth/password/reset", new { ticket, newPassword });

    private Task<HttpResponseMessage> LogInAsync(string phone, string password) =>
        PostAsync("/api/auth/login", new { login = phone, password });

    private string LastCodeSentTo(string phone) =>
        SixDigits().Match(_sms.Last(m => m.Phone == phone).Message).Value;

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    /// <summary>Asks for a reset code, verifies it, and returns the ticket.</summary>
    private async Task<string> TicketForAsync(string phone)
    {
        using (var request = await RequestResetAsync(phone))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        using var verify = await VerifyAsync(phone, LastCodeSentTo(phone));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        return (await JsonAsync(verify)).GetProperty("ticket").GetString()!;
    }

    /// <summary>The refresh and CSRF cookies a sign-in set.</summary>
    private static (string Refresh, string Csrf) CookiesOf(HttpResponseMessage response)
    {
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);

        return (
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString());
    }

    private async Task<(string Refresh, string Csrf)> SignInAsync(string phone, string password)
    {
        using var response = await LogInAsync(phone, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return CookiesOf(response);
    }

    private async Task<HttpResponseMessage> SessionPostAsync(string path, (string Refresh, string Csrf) session)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        request.Headers.Add("X-CSRF-Token", session.Csrf);

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString();

    /// <summary>Lets the resend cooldown pass without waiting a minute: the cooldown is read off the code's TTL.</summary>
    private async Task EndCooldownAsync(string phone) =>
        Assert.True(await _redis.GetDatabase().KeyExpireAsync(OtpCacheKey.ForCode(phone, OtpPurpose.Reset), TimeSpan.FromMinutes(3)));

    // ------------------------------------------------------------------ the whole flow

    [Fact]
    public async Task AForgottenPasswordIsResetBySms_AndEverySessionFromBeforeItEnds()
    {
        var phone = await CreateUserAsync();
        var before = await SignInAsync(phone, OldPassword);

        var ticket = await TicketForAsync(phone);
        Assert.Contains("password reset", _sms.Last(m => m.Phone == phone).Message, StringComparison.Ordinal);

        using (var reset = await ResetAsync(ticket))
        {
            // 204 and no session: the user signs in with the new password.
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.False(reset.Headers.Contains("Set-Cookie"));
        }

        using (var old = await LogInAsync(phone, OldPassword))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        }

        await SignInAsync(phone, NewPassword);

        // The refresh token issued before the reset is dead (SecurityStamps.RotateAsync).
        using (var refresh = await SessionPostAsync("/api/auth/session/refresh", before))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        var userId = await UserIdOf(phone);
        Assert.Equal(
            PasswordResetHandler.RevokedReason,
            await QuerySingleAsync<string>(
                "SELECT TOP (1) RevokedReason FROM auth.RefreshTokens WHERE UserId = @userId ORDER BY Id;",
                new { userId }));

        // One outbox row, ids only.
        var payload = await QuerySingleAsync<string>(
            "SELECT Payload FROM core.OutboxMessages WHERE EventType = 'PasswordReset' AND AggregateId = @id;",
            new { id = userId.ToString() });
        Assert.Equal(["userId"], JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TheCodeGoesInTheAccountsLanguage_NotThePages()
    {
        var phone = await CreateUserAsync(language: "si");

        using var request = await RequestResetAsync(phone, language: "en");

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Contains("මුරපදය", _sms.Last(m => m.Phone == phone).Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ no enumeration

    [Fact]
    public async Task UnknownAndMfaRequiredNumbersGetTheSameAnswerAsARealOne_AndNoSms()
    {
        var real = await CreateUserAsync();
        var unknown = NewPhone();
        var staff = await CreateUserAsync(mfaRequired: true);

        var answers = new List<(string Phone, HttpStatusCode Status, string Body, TimeSpan Took)>();
        foreach (var phone in new[] { real, unknown, staff })
        {
            var clock = Stopwatch.StartNew();
            using var response = await RequestResetAsync(phone);
            answers.Add((phone, response.StatusCode, await response.Content.ReadAsStringAsync(), clock.Elapsed));
        }

        Assert.All(answers, a => Assert.Equal((HttpStatusCode.Accepted, string.Empty), (a.Status, a.Body)));
        Assert.All(answers, a => Assert.True(a.Took >= PasswordResetHandler.RequestFloor, $"{a.Phone} answered in {a.Took}"));
        Assert.True(
            answers.Max(a => a.Took) - answers.Min(a => a.Took) < TimeSpan.FromMilliseconds(750),
            string.Join(", ", answers.Select(a => a.Took)));

        Assert.Single(_sms, m => m.Phone == real);
        Assert.DoesNotContain(_sms, m => m.Phone == unknown || m.Phone == staff);

        // A guessed code is answered alike too: a code exists for each, so none says "no code".
        var guesses = new List<string>();
        foreach (var phone in new[] { real, unknown, staff })
        {
            using var guess = await VerifyAsync(phone, WrongCode(LastCodeSentTo(real)));
            Assert.Equal(HttpStatusCode.BadRequest, guess.StatusCode);
            guesses.Add(await guess.Content.ReadAsStringAsync());
        }

        Assert.Single(guesses.Distinct());
        Assert.Equal("otp_incorrect", JsonDocument.Parse(guesses[0]).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnMfaRequiredAccountCannotResetEvenWithATicket()
    {
        // A ticket issued before the account became MfaRequired - the one way it could hold one.
        var phone = await CreateUserAsync(mfaRequired: true);
        var userId = await UserIdOf(phone);
        var stamp = await QuerySingleAsync<string>("SELECT SecurityStamp FROM auth.Users WHERE Id = @userId;", new { userId });

        var ticket = Guid.NewGuid().ToString("N");
        var tickets = _host.Services.GetRequiredService<IPasswordResetTickets>();
        Assert.True(await tickets.StoreAsync(ticket, new PasswordResetTicket(userId, stamp), TimeSpan.FromMinutes(10)));

        using var reset = await ResetAsync(ticket);

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal("reset_ticket_invalid", await CodeOf(reset));

        using var old = await LogInAsync(phone, OldPassword);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    [Theory]
    [InlineData("Suspended", false)]
    [InlineData("Deactivated", false)]
    [InlineData("Active", true)]
    public async Task SuspendedOrDeletedAccountsGetNoCode(string status, bool isDeleted)
    {
        var phone = await CreateUserAsync(status: status, isDeleted: isDeleted);

        using var response = await RequestResetAsync(phone);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
    }

    [Fact]
    public async Task AnAccountSuspendedAfterItsTicketWasIssuedCannotReset()
    {
        var phone = await CreateUserAsync();
        var ticket = await TicketForAsync(phone);
        await ExecuteAsync("UPDATE auth.Users SET Status = 'Suspended' WHERE PhoneNumber = @phone;", new { phone });

        using var reset = await ResetAsync(ticket);

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal("reset_ticket_invalid", await CodeOf(reset));
    }

    // ------------------------------------------------------------------ the ticket

    [Fact]
    public async Task ATicketWorksOnlyOnce()
    {
        var phone = await CreateUserAsync();
        var ticket = await TicketForAsync(phone);

        using (var first = await ResetAsync(ticket))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var replay = await ResetAsync(ticket, "Another#2028Password");

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("reset_ticket_invalid", await CodeOf(replay));

        await SignInAsync(phone, NewPassword);
    }

    [Fact]
    public async Task ATicketIssuedBeforeALogOutEverywhereIsRefused()
    {
        // Logging out everywhere is a decision the holder took after the ticket was issued; a ticket
        // from before it must not undo it.
        var phone = await CreateUserAsync();
        var session = await SignInAsync(phone, OldPassword);
        var ticket = await TicketForAsync(phone);

        using (var logoutAll = await SessionPostAsync("/api/auth/session/logout-all", session))
        {
            Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);
        }

        using var reset = await ResetAsync(ticket);

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal("reset_ticket_invalid", await CodeOf(reset));
        await SignInAsync(phone, OldPassword);
    }

    [Fact]
    public async Task ARefusedPasswordGetsSignupsErrors_AndDoesNotSpendTheTicket()
    {
        var phone = await CreateUserAsync();
        var ticket = await TicketForAsync(phone);

        using (var weak = await ResetAsync(ticket, "short"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            var body = await JsonAsync(weak);
            Assert.Equal("password_rejected", body.GetProperty("code").GetString());
            Assert.Contains(body.GetProperty("failures").EnumerateArray(), f => f.GetString() == "TooShort");
        }

        using (var personal = await ResetAsync(ticket, "Aa1!" + phone))
        {
            Assert.Contains(
                (await JsonAsync(personal)).GetProperty("failures").EnumerateArray(),
                f => f.GetString() == "ContainsPersonalData");
        }

        using var good = await ResetAsync(ticket);
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
    }

    [Fact]
    public async Task SignupAndResetCodesAndTicketsDoNotCross()
    {
        var phone = await CreateUserAsync();

        // A signup code is not a reset code.
        using (var signupRequest = await PostAsync("/api/auth/otp/request", new { phoneNumber = phone, purpose = "signup" }))
        {
            Assert.Equal(HttpStatusCode.Accepted, signupRequest.StatusCode);
        }

        var signupCode = LastCodeSentTo(phone);
        using (var asReset = await VerifyAsync(phone, signupCode))
        {
            Assert.Equal(HttpStatusCode.BadRequest, asReset.StatusCode);
            Assert.Equal("otp_not_found", await CodeOf(asReset));
        }

        // A signup ticket is not a reset ticket.
        string signupTicket;
        using (var signupVerify = await VerifyAsync(phone, signupCode, purpose: "signup"))
        {
            Assert.Equal(HttpStatusCode.OK, signupVerify.StatusCode);
            signupTicket = (await JsonAsync(signupVerify)).GetProperty("ticket").GetString()!;
        }

        using (var resetWithSignupTicket = await ResetAsync(signupTicket))
        {
            Assert.Equal(HttpStatusCode.BadRequest, resetWithSignupTicket.StatusCode);
            Assert.Equal("reset_ticket_invalid", await CodeOf(resetWithSignupTicket));
        }

        // A reset code is not a signup code.
        using (var resetRequest = await RequestResetAsync(phone))
        {
            Assert.Equal(HttpStatusCode.Accepted, resetRequest.StatusCode);
        }

        var resetCode = LastCodeSentTo(phone);
        using (var asSignup = await VerifyAsync(phone, resetCode, purpose: "signup"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, asSignup.StatusCode);
            Assert.Equal("otp_not_found", await CodeOf(asSignup));
        }

        // And a reset ticket is not a signup ticket.
        string resetTicket;
        using (var resetVerify = await VerifyAsync(phone, resetCode))
        {
            Assert.Equal(HttpStatusCode.OK, resetVerify.StatusCode);
            resetTicket = (await JsonAsync(resetVerify)).GetProperty("ticket").GetString()!;
        }

        using var client = NewClient();
        var documents = await client.GetFromJsonAsync<JsonElement[]>("/api/legal/current?language=en");
        using var signUp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            phoneNumber = phone,
            verificationTicket = resetTicket,
            fullName = "Cross Purpose",
            password = NewPassword,
            preferredLanguage = "en",
            acceptedDocumentHashes = documents!.Select(d => d.GetProperty("contentSha256").GetString()),
        });

        Assert.Equal(HttpStatusCode.BadRequest, signUp.StatusCode);
        Assert.Equal("phone_not_verified", await CodeOf(signUp));

        // The reset ticket, refused there, still resets.
        using var reset = await ResetAsync(resetTicket);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    // ------------------------------------------------------------------ guessing and the bill

    [Fact]
    public async Task AWrongCodeFiveTimesBurnsIt()
    {
        var phone = await CreateUserAsync();
        using (var request = await RequestResetAsync(phone))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var code = LastCodeSentTo(phone);

        for (var remaining = 4; remaining >= 1; remaining--)
        {
            using var wrong = await VerifyAsync(phone, WrongCode(code));
            var body = await JsonAsync(wrong);
            Assert.Equal("otp_incorrect", body.GetProperty("code").GetString());
            Assert.Equal(remaining, body.GetProperty("attemptsRemaining").GetInt32());
        }

        using (var fifth = await VerifyAsync(phone, WrongCode(code)))
        {
            Assert.Equal("otp_attempts_exhausted", await CodeOf(fifth));
        }

        using var right = await VerifyAsync(phone, code);
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);
        Assert.Equal("otp_not_found", await CodeOf(right));
    }

    [Fact]
    public async Task AFourthRequestWithinTheHourIs429_ForARealNumberAndAnUnknownOneAlike()
    {
        var real = await CreateUserAsync();
        var unknown = NewPhone();

        foreach (var phone in new[] { real, unknown })
        {
            for (var i = 0; i < 3; i++)
            {
                using var allowed = await RequestResetAsync(phone);
                Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
                await EndCooldownAsync(phone);
            }

            using var fourth = await RequestResetAsync(phone);
            Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
            Assert.True(fourth.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        }

        Assert.Equal(3, _sms.Count(m => m.Phone == real));
        Assert.DoesNotContain(_sms, m => m.Phone == unknown);
    }

    // ------------------------------------------------------------------ lockout

    [Fact]
    public async Task ALockedAccountCanReset_AndThenSignsInWithTheNewPassword()
    {
        var phone = await CreateUserAsync();
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await LogInAsync(phone, "Wrong#Password1");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (var locked = await LogInAsync(phone, OldPassword))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        }

        using (var reset = await ResetAsync(await TicketForAsync(phone)))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        await SignInAsync(phone, NewPassword);
    }

    [Fact]
    public async Task AResetStartsTheFailureCountAgain()
    {
        // Four failures before the reset and one after must not add up to a lockout.
        var phone = await CreateUserAsync();
        for (var i = 0; i < 4; i++)
        {
            using var wrong = await LogInAsync(phone, "Wrong#Password1");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (var reset = await ResetAsync(await TicketForAsync(phone)))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using (var oneMore = await LogInAsync(phone, "Wrong#Password1"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oneMore.StatusCode);
        }

        await SignInAsync(phone, NewPassword);
    }
}

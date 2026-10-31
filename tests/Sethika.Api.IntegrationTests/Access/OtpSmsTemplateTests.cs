using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Notifications;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// The one-time-code SMS comes from <c>notify.Templates</c> in the recipient's language (plan 5.6),
/// against the published templates in real SQL Server and real Redis.
/// </summary>
/// <remarks>
/// The expected text is read from the database, and the script of the message is checked as well:
/// a test that compared the SMS only with whatever the table holds would pass with the wrong
/// language in the wrong row.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed partial class OtpSmsTemplateTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "otp-sms-template-test-signing-key-at-least-32-bytes!";

    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
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

    private static string NewPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>An OtpService over the real database and Redis, logging into <paramref name="logger"/>.</summary>
    private OtpService NewService(RecordingLogger<OtpService> logger) =>
        new(
            _redis,
            new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Anonymous()),
            new RecordingSmsSender(_sms),
            Options.Create(new OtpOptions()),
            logger);

    private async Task<string> TemplateAsync(string code, string language)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @code AND Channel = 'SMS' AND Language = @language;",
            new { code, language }) ?? throw new InvalidOperationException($"No {code} template in {language}.");
    }

    /// <summary>The message the phone received, checked to carry exactly one six-digit code.</summary>
    private (string Message, string Code) Received(string phone)
    {
        var message = Assert.Single(_sms, m => m.Phone == phone).Message;
        var code = SixDigits().Match(message).Value;

        Assert.Equal(6, code.Length);
        Assert.DoesNotContain("{code}", message, StringComparison.Ordinal);
        return (message, code);
    }

    /// <summary>Sinhala block U+0D80-U+0DFF, Tamil U+0B80-U+0BFF.</summary>
    private static bool IsInScript(string message, string language) => language switch
    {
        "si" => message.Any(c => c is >= '඀' and <= '෿'),
        "ta" => message.Any(c => c is >= '஀' and <= '௿'),
        _ => message.All(char.IsAscii),
    };

    // ------------------------------------------------------------------ tests

    [Theory]
    [InlineData("si")]
    [InlineData("ta")]
    [InlineData("en")]
    public async Task ASignupCodeArrivesInTheRequestedLanguage(string language)
    {
        var phone = NewPhone();
        using var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/otp/request", new { phoneNumber = phone, purpose = "signup", language });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var (message, code) = Received(phone);

        Assert.Equal((await TemplateAsync("OTP_SIGNUP", language)).Replace("{code}", code, StringComparison.Ordinal), message);
        Assert.True(IsInScript(message, language), $"'{message}' is not in {language}.");
    }

    [Fact]
    public async Task ALanguageWithoutATemplateGetsEnglish_AndAWarning()
    {
        // The API accepts only en, si and ta, so a language with no template reaches OtpService only
        // when a template is missing. "fr" stands in for that: notify.Templates cannot hold it.
        var phone = NewPhone();
        var logger = new RecordingLogger<OtpService>();

        var sent = await NewService(logger).SendAsync(phone, OtpPurpose.Signup, "fr");

        Assert.True(sent.WasSent);
        var (message, code) = Received(phone);
        Assert.Equal((await TemplateAsync("OTP_SIGNUP", "en")).Replace("{code}", code, StringComparison.Ordinal), message);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("OTP_SIGNUP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APurposeWithoutAnEnglishTemplateSendsNothing_AndSaysSoLoudly()
    {
        // Every purpose is seeded, so OTP_LOGIN is switched off for this test, in every language,
        // and switched back on after it: a purpose with no active English template. Nothing is sent,
        // nothing is stored, the send budget is untouched, and the caller hears "unavailable". The
        // database tests share one collection and never run alongside this one.
        var phone = NewPhone();
        var logger = new RecordingLogger<OtpService>();

        await SetLoginTemplatesActiveAsync(false);
        OtpSendResult sent;
        try
        {
            sent = await NewService(logger).SendAsync(phone, OtpPurpose.Login, "si");
        }
        finally
        {
            await SetLoginTemplatesActiveAsync(true);
        }

        Assert.Equal(OtpSendOutcome.Unavailable, sent.Outcome);
        Assert.DoesNotContain(_sms, m => m.Phone == phone);
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(OtpCacheKey.ForCode(phone, OtpPurpose.Login)));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(OtpCacheKey.ForSendCount(phone, OtpPurpose.Login)));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("OTP_LOGIN", StringComparison.Ordinal));
    }

    private async Task SetLoginTemplatesActiveAsync(bool active)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE notify.Templates SET IsActive = @active WHERE Code = 'OTP_LOGIN' AND Channel = 'SMS';",
            new { active });
    }
}

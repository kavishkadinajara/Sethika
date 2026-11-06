using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Files;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Boots the real host to prove the access services are registered, configured as the plan says,
/// and that the two unsafe SMS settings stop it from starting outside Development.
/// </summary>
/// <remarks>
/// A service that is written and tested but never registered passes every unit test and fails
/// the first request that needs it. So does a guard that is written but only runs when something
/// is first resolved - which is why the refusals are asserted on startup, not on a call.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class AccessServiceRegistrationTests(SethikaTestDatabase database)
{
    private const string SigningKey = "registration-test-signing-key-at-least-32-bytes!!";

    private WebApplicationFactory<Program> Host(string environment, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);

            // Required outside Development (plan 02, B4). Set here so each refusal test fails for its
            // own reason; the test of this one setting overrides it with an empty value.
            builder.UseSetting("Files:LocalRoot", Path.Combine(Path.GetTempPath(), "sethika-registration-files"));

            // Required outside Development too (plan 09, M10); PaymentConfigurationTests tests the refusals.
            builder.UseSetting("Payments:PayHere:ApiPublicUrl", "https://api.sethika.example");
            builder.UseSetting("Payments:PayHere:WebPublicUrl", "https://sethika.example");

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    /// <summary>Starts the host, expecting it to refuse, and returns every message in the failure.</summary>
    private static string StartupFailure(WebApplicationFactory<Program> host)
    {
        var failure = Record.Exception(() => host.Services);

        Assert.NotNull(failure);

        var messages = new List<string>();
        for (var ex = failure; ex is not null; ex = ex.InnerException)
        {
            messages.Add(ex.Message);
        }

        return string.Join(" | ", messages);
    }

    [Fact]
    public void EveryAccessServiceResolvesInDevelopment()
    {
        // Development also turns on ValidateOnBuild, so a registration whose own dependencies are
        // missing fails here at startup rather than on the first request that needs it.
        using var host = Host(Environments.Development);
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<OtpService>(services.GetRequiredService<IOtpService>());
        Assert.IsType<ConsoleSmsSender>(services.GetRequiredService<ISmsSender>());
        Assert.IsType<SethikaPasswordPolicy>(services.GetRequiredService<IPasswordPolicy>());
        Assert.IsType<PhoneNumberNormalizer>(services.GetRequiredService<IPhoneNumberNormalizer>());
        Assert.IsType<PasswordHasher<User>>(services.GetRequiredService<IPasswordHasher<User>>());
        Assert.IsType<JwtTokenService>(services.GetRequiredService<ITokenService>());
    }

    [Fact]
    public async Task TheRegisteredTokenServiceIssuesTokensTheHostAccepts()
    {
        // The service's key comes from JwtOptions and the bearer validation's from Jwt:SigningKey
        // read directly in Program.cs. This is the test that fails if the two stop being the same.
        using var host = Host(Environments.Development);

        var tokens = host.Services.GetRequiredService<ITokenService>().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);

        using var response = await client.GetAsync("/api/me/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void RefusesToStartWithTheConsoleSenderOutsideDevelopment()
    {
        // No Sms:Provider at all, which is how a fresh App Service arrives: the default is the
        // console sender, so a forgotten setting is refused rather than silently sending nothing.
        using var host = Host(Environments.Production);

        Assert.Contains("Sms:Provider is Console outside Development", StartupFailure(host), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToStartWithOneTimeCodesLoggedOutsideDevelopment()
    {
        using var host = Host(
            Environments.Production,
            ("Sms:Provider", SmsOptions.NotifyLkProvider),
            ("Otp:LogCodesForDevelopment", "true"));

        Assert.Contains("Otp:LogCodesForDevelopment", StartupFailure(host), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToStartWithAnUnknownSmsProvider()
    {
        using var host = Host(Environments.Development, ("Sms:Provider", "Twilio"));

        Assert.Contains("'Twilio' is not recognised", StartupFailure(host), StringComparison.Ordinal);
    }

    [Fact]
    public void UsesNotifyLkOutsideDevelopment_ThroughAFactoryClientPointedAtTheGateway()
    {
        using var host = Host(Environments.Production, ("Sms:Provider", SmsOptions.NotifyLkProvider));
        using var scope = host.Services.CreateScope();

        Assert.IsType<NotifyLkSmsSender>(scope.ServiceProvider.GetRequiredService<ISmsSender>());

        // A typed client is registered under its service type's name.
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISmsSender));

        Assert.Equal(new Uri(new SmsOptions().NotifyLkBaseUrl), client.BaseAddress);
    }

    [Fact]
    public void UsesTextLkWhenConfigured_ThroughAFactoryClientPointedAtTheGateway()
    {
        using var host = Host(Environments.Production, ("Sms:Provider", SmsOptions.TextLkProvider));
        using var scope = host.Services.CreateScope();

        Assert.IsType<TextLkSmsSender>(scope.ServiceProvider.GetRequiredService<ISmsSender>());

        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISmsSender));

        Assert.Equal(new Uri(new SmsOptions().TextLkBaseUrl), client.BaseAddress);
    }

    [Fact]
    public void RefusesToStartWithoutAFileRootOutsideDevelopment()
    {
        using var host = Host(
            Environments.Production,
            ("Sms:Provider", SmsOptions.NotifyLkProvider),
            ("Files:LocalRoot", ""));

        Assert.Contains("Files:LocalRoot is not set", StartupFailure(host), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsFilesOnLocalDiskWithADefaultRootInDevelopment()
    {
        using var host = Host(Environments.Development, ("Files:LocalRoot", ""));

        Assert.Equal(LocalFileStorage.Provider, Assert.IsType<LocalFileStorage>(host.Services.GetRequiredService<IFileStorage>()).ProviderName);
    }

    [Fact]
    public void LocksAnAccountForFifteenMinutesAfterFiveFailures()
    {
        using var host = Host(Environments.Development);

        var lockout = host.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        Assert.Equal(5, lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), lockout.DefaultLockoutTimeSpan);
    }

    [Fact]
    public void HashesPasswordsWithPbkdf2HmacSha512At210000Iterations()
    {
        // The plan and the report both say 210 000 iterations of PBKDF2-HMAC-SHA512. Identity's
        // default is 100 000, so the claim is only true if the configuration says so. Read it back
        // out of a real hash - version marker, PRF and iteration count sit in its first nine bytes.
        using var host = Host(Environments.Development);

        var hasher = host.Services.GetRequiredService<IPasswordHasher<User>>();
        var user = new User();

        var hash = hasher.HashPassword(user, "Dana!2026Sethika");
        var bytes = Convert.FromBase64String(hash);

        Assert.Equal(0x01, bytes[0]);                                                      // Identity v3 format
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)));           // KeyDerivationPrf.HMACSHA512
        Assert.Equal(210_000u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)));     // iteration count
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, "Dana!2026Sethika"));
    }
}

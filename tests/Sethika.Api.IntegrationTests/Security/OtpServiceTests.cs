using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;
using Xunit;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Exercises the real Redis behaviour: TTLs, counters and deletion. These would all pass against a
/// mocked database while the shipped code failed, because what is being tested is the interaction
/// with Redis, not the arithmetic around it.
/// </summary>
/// <remarks>
/// No SQL, so this class deliberately stays out of the serialized database collection. Each test
/// uses a unique phone number, so nothing here collides with a parallel test or with leftover keys.
/// The Redis connection comes from a shared fixture: building one per test method cost about five
/// seconds each against local Redis 3.2, which dwarfed the tests themselves.
/// </remarks>
[Collection(RedisCollection.Name)]
public sealed class OtpServiceTests
{
    private readonly List<(string PhoneNumber, string Message)> _sent = [];
    private readonly IConnectionMultiplexer _redis;

    public OtpServiceTests(RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis.Redis;
    }

    /// <summary>Records what was sent instead of sending it, and exposes the code for assertions.</summary>
    private sealed class RecordingSmsSender(List<(string PhoneNumber, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(
            string phoneNumber,
            string message,
            string? dedupKey = null,
            CancellationToken cancellationToken = default)
        {
            sent.Add((phoneNumber, message));
            return Task.CompletedTask;
        }
    }

    /// <summary>A number nothing else in the suite will use.</summary>
    private static string NewPhoneNumber() =>
        "+9477" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private OtpService CreateService(Action<OtpOptions>? configure = null)
    {
        var options = new OtpOptions();
        configure?.Invoke(options);

        return new OtpService(
            _redis,
            new RecordingSmsSender(_sent),
            Options.Create(options),
            NullLogger<OtpService>.Instance);
    }

    /// <summary>Pulls the six-digit code out of the message the sender recorded.</summary>
    private string LastCode()
    {
        var message = _sent[^1].Message;
        var digits = new string([.. message.Where(char.IsDigit)]);

        return digits;
    }

    [Fact]
    public async Task SendsACodeAndAcceptsIt()
    {
        var phone = NewPhoneNumber();
        var service = CreateService();

        var send = await service.SendAsync(phone, OtpPurpose.Signup, "en");

        Assert.True(send.WasSent);
        Assert.Single(_sent);

        var verify = await service.VerifyAsync(phone, OtpPurpose.Signup, LastCode());

        Assert.True(verify.IsVerified);
    }

    [Fact]
    public async Task NeverStoresTheCodeItself()
    {
        // The whole point of hashing: a Redis dump must not yield working codes.
        var phone = NewPhoneNumber();
        await CreateService().SendAsync(phone, OtpPurpose.Signup, "en");

        var code = LastCode();
        var stored = await _redis.GetDatabase()
            .HashGetAsync(OtpCacheKey.ForCode(phone, OtpPurpose.Signup), "h");

        Assert.False(stored.IsNullOrEmpty);
        Assert.NotEqual(code, (string)stored!);
        Assert.Equal(64, ((string)stored!).Length);   // hex SHA-256
    }

    [Fact]
    public async Task ConsumesTheCodeSoItCannotBeReplayed()
    {
        var phone = NewPhoneNumber();
        var service = CreateService();
        await service.SendAsync(phone, OtpPurpose.Signup, "en");
        var code = LastCode();

        Assert.True((await service.VerifyAsync(phone, OtpPurpose.Signup, code)).IsVerified);

        var replay = await service.VerifyAsync(phone, OtpPurpose.Signup, code);

        Assert.Equal(OtpVerifyOutcome.NotFound, replay.Outcome);
    }

    [Fact]
    public async Task ACodeIssuedForSignupDoesNotSatisfyAStepUpChallenge()
    {
        // The reason the purpose is in the key. An attacker can have a signup code sent to their own
        // phone at will; if that code verified a step-up, it would unlock a sensitive action on an
        // account they are attacking.
        var phone = NewPhoneNumber();
        var service = CreateService();

        await service.SendAsync(phone, OtpPurpose.Signup, "en");
        var signupCode = LastCode();

        var crossUse = await service.VerifyAsync(phone, OtpPurpose.StepUp, signupCode);

        Assert.Equal(OtpVerifyOutcome.NotFound, crossUse.Outcome);

        // And the signup code is still usable for what it was issued for.
        Assert.True((await service.VerifyAsync(phone, OtpPurpose.Signup, signupCode)).IsVerified);
    }

    [Fact]
    public async Task DestroysTheCodeAfterTheAttemptBudgetIsSpent()
    {
        var phone = NewPhoneNumber();
        var service = CreateService(o => o.MaxVerifyAttempts = 3);
        await service.SendAsync(phone, OtpPurpose.Signup, "en");
        var realCode = LastCode();

        var first = await service.VerifyAsync(phone, OtpPurpose.Signup, "000000");
        var second = await service.VerifyAsync(phone, OtpPurpose.Signup, "000001");

        Assert.Equal(OtpVerifyOutcome.Incorrect, first.Outcome);
        Assert.Equal(2, first.AttemptsRemaining);
        Assert.Equal(1, second.AttemptsRemaining);

        var third = await service.VerifyAsync(phone, OtpPurpose.Signup, "000002");

        Assert.Equal(OtpVerifyOutcome.TooManyAttempts, third.Outcome);

        // The correct code must not work either: the code is gone, not merely locked out, so an
        // attacker cannot keep guessing for the rest of its five-minute life.
        var afterBudget = await service.VerifyAsync(phone, OtpPurpose.Signup, realCode);

        Assert.Equal(OtpVerifyOutcome.NotFound, afterBudget.Outcome);
    }

    [Fact]
    public async Task RefusesAResendInsideTheCooldown()
    {
        var phone = NewPhoneNumber();
        var service = CreateService();

        Assert.True((await service.SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);

        var second = await service.SendAsync(phone, OtpPurpose.Signup, "en");

        Assert.Equal(OtpSendOutcome.CooldownActive, second.Outcome);
        Assert.NotNull(second.RetryAfter);
        Assert.Single(_sent);   // and crucially, nothing was billed
    }

    [Fact]
    public async Task CapsSendsPerWindowSoTheFormCannotBeUsedToRunUpABill()
    {
        var phone = NewPhoneNumber();

        // No cooldown, so the send cap is what is under test rather than the cooldown.
        var service = CreateService(o =>
        {
            o.ResendCooldown = TimeSpan.Zero;
            o.MaxSendsPerWindow = 2;
        });

        Assert.True((await service.SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);
        Assert.True((await service.SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);

        var third = await service.SendAsync(phone, OtpPurpose.Signup, "en");

        Assert.Equal(OtpSendOutcome.RateLimited, third.Outcome);
        Assert.NotNull(third.RetryAfter);
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public async Task AResendDoesNotRestoreAnAlreadySpentAttemptBudget()
    {
        // Otherwise the attempt cap is bypassable: guess five times, resend, guess five more.
        // A fresh code must come with a fresh budget but must not revive the old code.
        var phone = NewPhoneNumber();
        var service = CreateService(o =>
        {
            o.ResendCooldown = TimeSpan.Zero;
            o.MaxVerifyAttempts = 2;
        });

        await service.SendAsync(phone, OtpPurpose.Signup, "en");
        var firstCode = LastCode();

        await service.VerifyAsync(phone, OtpPurpose.Signup, "000000");
        await service.VerifyAsync(phone, OtpPurpose.Signup, "000001");

        // Budget spent, first code destroyed.
        Assert.Equal(
            OtpVerifyOutcome.NotFound,
            (await service.VerifyAsync(phone, OtpPurpose.Signup, firstCode)).Outcome);

        await service.SendAsync(phone, OtpPurpose.Signup, "en");
        var secondCode = LastCode();

        Assert.NotEqual(firstCode, secondCode);
        Assert.True((await service.VerifyAsync(phone, OtpPurpose.Signup, secondCode)).IsVerified);
    }

    [Fact]
    public async Task ReportsNotFoundForANumberWithNoOutstandingCode()
    {
        var result = await CreateService().VerifyAsync(NewPhoneNumber(), OtpPurpose.Login, "123456");

        Assert.Equal(OtpVerifyOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task SetsAnExpiryOnTheCodeSoItCannotOutliveItsLifetime()
    {
        var phone = NewPhoneNumber();
        var service = CreateService(o => o.Lifetime = TimeSpan.FromMinutes(5));

        await service.SendAsync(phone, OtpPurpose.Signup, "en");

        var ttl = await _redis.GetDatabase()
            .KeyTimeToLiveAsync(OtpCacheKey.ForCode(phone, OtpPurpose.Signup));

        // A missing TTL would mean the code lives until something else evicts it - the failure mode
        // that makes an expiry-based design silently permanent.
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GeneratesACodeOfTheConfiguredLengthWithLeadingZeroesKept()
    {
        // PadLeft matters: formatting 1234 as "1234" would produce a four-digit code and, worse, a
        // shorter guessing space than the configuration claims.
        var service = CreateService(o => o.CodeLength = 6);

        for (var i = 0; i < 40; i++)
        {
            _sent.Clear();
            await service.SendAsync(NewPhoneNumber(), OtpPurpose.Signup, "en");

            Assert.Equal(6, LastCode().Length);
        }
    }

    [Theory]
    [InlineData("si", "සත්‍යාපන")]
    [InlineData("ta", "சரிபார்ப்பு")]
    [InlineData("en", "verification")]
    public async Task SendsTheMessageInTheRequestedLanguage(string language, string expectedFragment)
    {
        await CreateService().SendAsync(NewPhoneNumber(), OtpPurpose.Signup, language);

        Assert.Contains(expectedFragment, _sent[^1].Message, StringComparison.Ordinal);
    }

    /// <summary>Sends and verifies a code, returning the ticket the verification issued.</summary>
    private async Task<(string Phone, string Ticket)> VerifiedTicketAsync(OtpService service, OtpPurpose purpose)
    {
        var phone = NewPhoneNumber();
        await service.SendAsync(phone, purpose, "en");

        var verify = await service.VerifyAsync(phone, purpose, LastCode());

        Assert.True(verify.IsVerified);
        Assert.False(string.IsNullOrEmpty(verify.Ticket));

        return (phone, verify.Ticket!);
    }

    [Fact]
    public async Task AVerificationTicketRedeemsExactlyOnce()
    {
        // Single use is what stops one verified phone from being spent on two signups, and stops a
        // ticket lifted from a request log from being replayed within its lifetime.
        var service = CreateService();
        var (phone, ticket) = await VerifiedTicketAsync(service, OtpPurpose.Signup);

        Assert.Equal(OtpTicketOutcome.Redeemed, await service.RedeemTicketAsync(phone, OtpPurpose.Signup, ticket));
        Assert.Equal(OtpTicketOutcome.Invalid, await service.RedeemTicketAsync(phone, OtpPurpose.Signup, ticket));
    }

    [Fact]
    public async Task ConcurrentRedemptionsOfOneTicketSucceedOnlyOnce()
    {
        // The reason redemption is a server-side script: GET then DEL from here would let parallel
        // requests all read the ticket before any of them deleted it.
        var service = CreateService();
        var (phone, ticket) = await VerifiedTicketAsync(service, OtpPurpose.Signup);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.RedeemTicketAsync(phone, OtpPurpose.Signup, ticket)));

        Assert.Single(outcomes, o => o == OtpTicketOutcome.Redeemed);
    }

    [Fact]
    public async Task ATicketIsBoundToItsPhoneAndPurpose()
    {
        var service = CreateService();
        var (phone, ticket) = await VerifiedTicketAsync(service, OtpPurpose.Signup);

        Assert.Equal(OtpTicketOutcome.Invalid, await service.RedeemTicketAsync(NewPhoneNumber(), OtpPurpose.Signup, ticket));
        Assert.Equal(OtpTicketOutcome.Invalid, await service.RedeemTicketAsync(phone, OtpPurpose.Reset, ticket));
        Assert.Equal(OtpTicketOutcome.Invalid, await service.RedeemTicketAsync(phone, OtpPurpose.Signup, ticket + "x"));

        // None of the refusals spent it.
        Assert.Equal(OtpTicketOutcome.Redeemed, await service.RedeemTicketAsync(phone, OtpPurpose.Signup, ticket));
    }

    [Fact]
    public async Task NeverStoresTheTicketItself_AndLetsItExpire()
    {
        var service = CreateService();
        var (phone, ticket) = await VerifiedTicketAsync(service, OtpPurpose.Signup);

        var key = OtpCacheKey.ForTicket(phone, OtpPurpose.Signup);
        var stored = (string)(await _redis.GetDatabase().StringGetAsync(key))!;
        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(key);

        Assert.NotEqual(ticket, stored);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ticket))),
            stored);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task TheVerifyResultNeverPrintsTheTicket()
    {
        var service = CreateService();
        var phone = NewPhoneNumber();
        await service.SendAsync(phone, OtpPurpose.Signup, "en");

        var verify = await service.VerifyAsync(phone, OtpPurpose.Signup, LastCode());

        Assert.DoesNotContain(verify.Ticket!, verify.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASendCounterLeftWithoutAnExpiryIsGivenOne()
    {
        // INCR and EXPIRE as two commands leave a window: a process that dies between them leaves a
        // counter that never expires, and that phone can never be sent a code again. Simulate the
        // crash by leaving a counter with no TTL, then send - the counter must come out expiring.
        var phone = NewPhoneNumber();
        var counter = OtpCacheKey.ForSendCount(phone, OtpPurpose.Signup);
        await _redis.GetDatabase().StringSetAsync(counter, 1);

        Assert.True((await CreateService().SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);

        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(counter);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
    }
}

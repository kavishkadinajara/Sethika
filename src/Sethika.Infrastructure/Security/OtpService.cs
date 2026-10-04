using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Caching;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Issues and checks one-time codes, holding them in Redis and never in SQL.
/// </summary>
/// <remarks>
/// Redis rather than a table: a code is a secret with a five-minute life, so persisting it puts a
/// live credential in the database, invites a cleanup job, and creates something to retain under
/// PDPA. Expiry is the storage engine's job here. The durable record of a verification is
/// <c>auth.Users.PhoneConfirmed</c> and <c>auth.LoginHistory</c>.
///
/// Only the SHA-256 of the code is stored, so a Redis dump - or an operator with MONITOR - does not
/// yield working codes. There is no salt: the input is six digits from a cryptographic RNG, the
/// entry lives five minutes, and a rainbow table over a million values is trivial either way. What
/// makes guessing infeasible is the attempt cap, not the hash.
///
/// A Redis outage means no code can be issued or checked, so signup and step-up stop. Password
/// login is unaffected. This is deliberately unlike the permission cache, which degrades to a SQL
/// read: there is no safe fallback for a one-time code, because the only fallback would be to
/// accept a code nobody issued.
/// </remarks>
public sealed partial class OtpService : IOtpService
{
    private const string CodeHashField = "h";
    private const string AttemptsField = "att";

    /// <summary>
    /// Deletes the ticket only if the presented hash matches, in one step.
    /// </summary>
    /// <remarks>
    /// GET then DEL from C# would let two requests with the same ticket both read it before either
    /// deleted it, and both be told it was valid. A script runs atomically on the server. It is a
    /// script rather than GETDEL because the local Redis is 3.2 and GETDEL arrived in 6.2. The
    /// comparison is of hashes of a 256-bit random value, so its timing tells an attacker nothing
    /// they could steer.
    /// </remarks>
    private const string RedeemTicketScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0";

    private readonly IConnectionMultiplexer _redis;
    private readonly ISmsSender _sms;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    /// <summary>Creates the service.</summary>
    public OtpService(
        IConnectionMultiplexer redis,
        ISmsSender sms,
        IOptions<OtpOptions> options,
        ILogger<OtpService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _redis = redis;
        _sms = sms;
        _logger = logger;
        _options = options.Value;
    }

    public Task<OtpSendResult> SendAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string language,
        CancellationToken cancellationToken = default) =>
        IssueAsync(phoneNumber, purpose, language, cancellationToken);

    public Task<OtpSendResult> SendDecoyAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default) =>
        IssueAsync(phoneNumber, purpose, language: null, cancellationToken);

    /// <summary>
    /// The one path both sends take, so a decoy cannot drift from a real send. A null
    /// <paramref name="language"/> is a decoy: the code is stored but neither logged nor sent.
    /// </summary>
    private async Task<OtpSendResult> IssueAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string? language,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var codeKey = OtpCacheKey.ForCode(phoneNumber, purpose);
        var sendCountKey = OtpCacheKey.ForSendCount(phoneNumber, purpose);

        try
        {
            var db = _redis.GetDatabase();

            // Cooldown is derived from the outstanding code's own TTL rather than a separate key,
            // so there is one fewer thing that can disagree with the code's lifetime.
            var existingTtl = await db.KeyTimeToLiveAsync(codeKey).ConfigureAwait(false);
            if (existingTtl is { } ttl && ttl > _options.Lifetime - _options.ResendCooldown)
            {
                var wait = ttl - (_options.Lifetime - _options.ResendCooldown);
                return OtpSendResult.Cooldown(wait);
            }

            // Increment before sending. Counting after would let a burst of concurrent requests all
            // observe the pre-send count and every one of them send, which is precisely the case
            // the cap exists to stop. The counter and its expiry are set in one step: see
            // RedisWindowCounter for the phone that could otherwise never receive a code again.
            var (sends, windowRemaining) = await RedisWindowCounter
                .IncrementAsync(db, sendCountKey, _options.SendWindow).ConfigureAwait(false);

            if (sends > _options.MaxSendsPerWindow)
            {
                return OtpSendResult.RateLimited(windowRemaining);
            }

            var code = GenerateCode(_options.CodeLength);

            // A fresh hash replaces any outstanding code and resets the attempt count, so a resend
            // cannot be used to top up a budget already spent guessing the previous code.
            await db.HashSetAsync(
                codeKey,
                [
                    new HashEntry(CodeHashField, Hash(code)),
                    new HashEntry(AttemptsField, 0),
                ]).ConfigureAwait(false);

            await db.KeyExpireAsync(codeKey, _options.Lifetime).ConfigureAwait(false);

            if (language is null)
            {
                return OtpSendResult.Sent();
            }

            if (_options.LogCodesForDevelopment)
            {
                LogDevelopmentCode(_logger, phoneNumber, purpose, code);
            }

            await _sms.SendAsync(
                phoneNumber,
                BuildMessage(code, purpose, language),
                dedupKey: null,
                cancellationToken).ConfigureAwait(false);

            return OtpSendResult.Sent();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(_logger, nameof(SendAsync), ex);
            return OtpSendResult.Unavailable();
        }
    }

    public async Task<OtpVerifyResult> VerifyAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        if (string.IsNullOrWhiteSpace(code))
        {
            return OtpVerifyResult.NotFound();
        }

        var codeKey = OtpCacheKey.ForCode(phoneNumber, purpose);

        try
        {
            var db = _redis.GetDatabase();

            var stored = await db.HashGetAsync(codeKey, CodeHashField).ConfigureAwait(false);
            if (stored.IsNullOrEmpty)
            {
                // Covers both "never issued" and "already expired or consumed". They are one answer
                // to the caller on purpose: distinguishing them tells an attacker whether a number
                // is mid-signup.
                return OtpVerifyResult.NotFound();
            }

            // Count the attempt before comparing, so a client that disconnects on a wrong guess
            // still pays for it. Incrementing afterwards would make the cap bypassable by
            // abandoning each request.
            var attempts = await db.HashIncrementAsync(codeKey, AttemptsField).ConfigureAwait(false);

            if (attempts > _options.MaxVerifyAttempts)
            {
                await db.KeyDeleteAsync(codeKey).ConfigureAwait(false);
                return OtpVerifyResult.TooManyAttempts();
            }

            if (!MatchesStoredHash(code, (string)stored!))
            {
                var remaining = (int)Math.Max(0, _options.MaxVerifyAttempts - attempts);

                if (remaining == 0)
                {
                    await db.KeyDeleteAsync(codeKey).ConfigureAwait(false);
                    return OtpVerifyResult.TooManyAttempts();
                }

                return OtpVerifyResult.Incorrect(remaining);
            }

            // Consume on success, so a code cannot be replayed within its remaining lifetime.
            await db.KeyDeleteAsync(codeKey).ConfigureAwait(false);

            // The ticket carries the verification to the next request. Only its hash is stored, for
            // the same reason only the code's hash is, and a later verification replaces it.
            var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

            await db.StringSetAsync(
                OtpCacheKey.ForTicket(phoneNumber, purpose),
                Hash(ticket),
                _options.TicketLifetime).ConfigureAwait(false);

            return OtpVerifyResult.Verified(ticket);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(_logger, nameof(VerifyAsync), ex);
            return OtpVerifyResult.Unavailable();
        }
    }

    public async Task<OtpTicketOutcome> RedeemTicketAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        if (string.IsNullOrWhiteSpace(ticket))
        {
            return OtpTicketOutcome.Invalid;
        }

        try
        {
            var deleted = await _redis.GetDatabase().ScriptEvaluateAsync(
                RedeemTicketScript,
                [OtpCacheKey.ForTicket(phoneNumber, purpose)],
                [Hash(ticket)]).ConfigureAwait(false);

            return (long)deleted == 1 ? OtpTicketOutcome.Redeemed : OtpTicketOutcome.Invalid;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(_logger, nameof(RedeemTicketAsync), ex);
            return OtpTicketOutcome.Unavailable;
        }
    }

    /// <summary>Generates a zero-padded numeric code using a cryptographic RNG.</summary>
    /// <remarks>
    /// <see cref="RandomNumberGenerator.GetInt32(int, int)"/> is uniform over the range and free of
    /// the modulo bias a naive <c>% 1000000</c> would introduce. <c>Random</c> would be predictable
    /// from a couple of observed codes, which for a login control is the whole game.
    /// </remarks>
    private static string GenerateCode(int length)
    {
        var exclusiveUpperBound = (int)Math.Pow(10, length);
        var value = RandomNumberGenerator.GetInt32(0, exclusiveUpperBound);

        return value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0');
    }

    /// <summary>Hex SHA-256 of a code or ticket, which is all that is ever stored.</summary>
    private static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    /// <summary>
    /// Compares the presented code against the stored hash in constant time.
    /// </summary>
    /// <remarks>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> rather than <c>==</c>: string
    /// comparison returns on the first differing byte, which leaks how much of a guess was correct
    /// and turns a million-value search into a digit-by-digit one.
    /// </remarks>
    private static bool MatchesStoredHash(string presentedCode, string storedHash)
    {
        var presented = Encoding.UTF8.GetBytes(Hash(presentedCode));
        var expected = Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    /// <summary>
    /// Builds the message body.
    /// </summary>
    /// <remarks>
    /// Placeholder text until <c>notify.Templates</c> is seeded per language in slice 5.6. The
    /// language is already threaded through so that adding the templates is a change here and
    /// nowhere else. A reset code says what it is for: someone who did not ask to reset their
    /// password should be able to tell from the message alone that somebody else did.
    /// </remarks>
    private static string BuildMessage(string code, OtpPurpose purpose, string language) => (purpose, language) switch
    {
        (OtpPurpose.Reset, "si") => $"ඔබගේ Sethika මුරපදය යළි සැකසීමේ කේතය: {code}",
        (OtpPurpose.Reset, "ta") => $"உங்கள் Sethika கடவுச்சொல் மீட்டமைப்புக் குறியீடு: {code}",
        (OtpPurpose.Reset, _) => $"Your Sethika password reset code is {code}",
        (_, "si") => $"ඔබගේ Sethika සත්‍යාපන කේතය: {code}",
        (_, "ta") => $"உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code}",
        _ => $"Your Sethika verification code is {code}",
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "One-time code store unavailable during {Operation}; the caller was told so.")]
    private static partial void LogStoreUnavailable(ILogger logger, string operation, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DEVELOPMENT ONLY - one-time code for {PhoneNumber} ({Purpose}) is {Code}.")]
    private static partial void LogDevelopmentCode(
        ILogger logger,
        string phoneNumber,
        OtpPurpose purpose,
        string code);
}

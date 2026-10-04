using System.Buffers.Text;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
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
///
/// The message is the <c>notify.Templates</c> row for the purpose, channel <c>SMS</c> and the
/// requested language, with <c>{code}</c> replaced. A language without a template falls back to
/// English with a warning; a purpose without an English template sends nothing and reports itself
/// unavailable, with an error in the log, rather than reach for a hard-coded text that nobody
/// reviews in the seed. Templates are read on every send, not cached: sends are capped at a handful
/// per number per hour, so the read costs nothing worth saving, and a cache would let a corrected
/// template keep going out wrong until it expired.
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

    private const string CodePlaceholder = "{code}";
    private const string FallbackLanguage = "en";

    // The requested language and English in one round trip; the caller picks.
    private const string TemplateSql =
        """
        SELECT t.Language AS Language,
               t.Body     AS Body
        FROM notify.Templates AS t
        WHERE t.Code = @code
          AND t.Channel = 'SMS'
          AND t.IsActive = 1
          AND (t.Language = @language OR t.Language = 'en');
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly IDbConnectionFactory _connections;
    private readonly ISmsSender _sms;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    /// <summary>Creates the service.</summary>
    public OtpService(
        IConnectionMultiplexer redis,
        IDbConnectionFactory connections,
        ISmsSender sms,
        IOptions<OtpOptions> options,
        ILogger<OtpService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _redis = redis;
        _connections = connections;
        _sms = sms;
        _logger = logger;
        _options = options.Value;
    }

    public Task<OtpSendResult> SendAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string language,
        CancellationToken cancellationToken = default) =>
        IssueAsync(phoneNumber, purpose, language, deliver: true, cancellationToken);

    public Task<OtpSendResult> SendDecoyAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default) =>
        IssueAsync(phoneNumber, purpose, SupportedLanguages.Default, deliver: false, cancellationToken);

    public async Task<OtpSendResult> SendUndisclosedAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string language,
        bool deliver,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();

        var result = await IssueAsync(phoneNumber, purpose, language, deliver, cancellationToken)
            .ConfigureAwait(false);

        // The SMS is the one step only a number with an account pays for: a database write and,
        // in production, a call to the gateway. Without a floor, a stopwatch on this request tells
        // anyone which numbers are registered however equal the answers are. A gateway slower than
        // the floor reopens the gap, so it is logged rather than hidden.
        var remaining = _options.UndisclosedSendDuration - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
        else if (deliver && result.WasSent)
        {
            LogFloorOverrun(_logger, purpose, -remaining);
        }

        return result;
    }

    private async Task<OtpSendResult> IssueAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string language,
        bool deliver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        // Read before anything is stored or counted, so a missing template neither leaves a code
        // nobody received nor spends the number's send budget. A send that delivers nothing reads
        // one too: the same round trip and the same Unavailable when it is missing, so neither its
        // timing nor its answer tells it from a real send.
        var template = await FindTemplateAsync(purpose, language, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return OtpSendResult.Unavailable();
        }

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

            if (!deliver)
            {
                // Stored all the same, so presenting a code for this number costs the same work and
                // burns after the same number of guesses as for a number that was sent one.
                return OtpSendResult.Sent();
            }

            if (_options.LogCodesForDevelopment)
            {
                LogDevelopmentCode(_logger, phoneNumber, purpose, code);
            }

            await _sms.SendAsync(
                phoneNumber,
                template.Replace(CodePlaceholder, code, StringComparison.Ordinal),
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
    /// The SMS body for <paramref name="purpose"/> in <paramref name="language"/>, or in English when
    /// that language has none; null, with the reason logged as an error, when there is nothing to send.
    /// </summary>
    private async Task<string?> FindTemplateAsync(OtpPurpose purpose, string language, CancellationToken cancellationToken)
    {
        var code = TemplateCode(purpose);

        List<(string Language, string Body)> rows;
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            rows = [.. await connection.QueryAsync<(string Language, string Body)>(new CommandDefinition(
                TemplateSql,
                new { code, language },
                cancellationToken: cancellationToken)).ConfigureAwait(false)];
        }
        catch (DbException ex)
        {
            LogTemplateUnreadable(_logger, code, ex);
            return null;
        }

        // Language is CHAR(2), so the comparison is exact only on trimmed values.
        var chosen = rows.Find(r => string.Equals(r.Language.Trim(), language, StringComparison.Ordinal));
        if (chosen.Body is null)
        {
            chosen = rows.Find(r => string.Equals(r.Language.Trim(), FallbackLanguage, StringComparison.Ordinal));
            if (chosen.Body is null)
            {
                LogTemplateMissing(_logger, code, language);
                return null;
            }

            LogTemplateFallback(_logger, code, language);
        }

        // A body that cannot carry the code would send an SMS that is no use to anyone, and cost money.
        if (!chosen.Body.Contains(CodePlaceholder, StringComparison.Ordinal))
        {
            LogTemplateWithoutCode(_logger, code, chosen.Language.Trim());
            return null;
        }

        return chosen.Body;
    }

    /// <summary>
    /// The <c>notify.Templates</c> code for a purpose.
    /// </summary>
    /// <remarks>
    /// Written out rather than built from the enum name, for the reason <see cref="OtpCacheKey"/>
    /// gives: a rename must not silently point at a template that does not exist.
    /// </remarks>
    private static string TemplateCode(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.Signup => "OTP_SIGNUP",
        OtpPurpose.Login => "OTP_LOGIN",
        OtpPurpose.StepUp => "OTP_STEPUP",
        OtpPurpose.Reset => "OTP_RESET",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown OTP purpose."),
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No active SMS template {TemplateCode} in '{Language}'; the English one was sent instead.")]
    private static partial void LogTemplateFallback(ILogger logger, string templateCode, string language);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No active SMS template {TemplateCode} in '{Language}' or in English; no code was sent. Seed notify.Templates.")]
    private static partial void LogTemplateMissing(ILogger logger, string templateCode, string language);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "SMS template {TemplateCode} in '{Language}' has no {{code}} placeholder; no code was sent.")]
    private static partial void LogTemplateWithoutCode(ILogger logger, string templateCode, string language);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not read SMS template {TemplateCode}; no code was sent.")]
    private static partial void LogTemplateUnreadable(ILogger logger, string templateCode, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "One-time code store unavailable during {Operation}; the caller was told so.")]
    private static partial void LogStoreUnavailable(ILogger logger, string operation, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A {Purpose} code took {Overrun} longer to send than the undisclosed-send floor; its timing can reveal that the number has an account. Raise Otp:UndisclosedSendDuration.")]
    private static partial void LogFloorOverrun(ILogger logger, OtpPurpose purpose, TimeSpan overrun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DEVELOPMENT ONLY - one-time code for {PhoneNumber} ({Purpose}) is {Code}.")]
    private static partial void LogDevelopmentCode(
        ILogger logger,
        string phoneNumber,
        OtpPurpose purpose,
        string code);
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.PasswordReset;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Password-reset tickets as Redis strings that expire by themselves: the key is the SHA-256 of the
/// ticket, the value the account id and security stamp it was issued for.
/// </summary>
/// <remarks>
/// Keyed by the ticket rather than the phone number, unlike the signup ticket (A17): the reset
/// request carries only the ticket, and what it must prove is an account, not a number.
/// Fails closed, like the step-up grants: an outage spends nothing and confirms nothing.
/// </remarks>
public sealed partial class RedisPasswordResetTickets(
    IConnectionMultiplexer redis,
    ILogger<RedisPasswordResetTickets> logger) : IPasswordResetTickets
{
    /// <summary>
    /// Reads and deletes in one step, so two requests with one ticket cannot both read it before
    /// either deletes it. A script rather than GETDEL, which needs Redis 6.2 (see OtpService).
    /// </summary>
    private const string RedeemScript =
        "local v = redis.call('GET', KEYS[1]) if v then redis.call('DEL', KEYS[1]) end return v";

    /// <summary>The key a ticket is stored under.</summary>
    public static string KeyFor(string ticket) =>
        "pwreset:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));

    public async Task<bool> StoreAsync(
        string ticket,
        PasswordResetTicket issuedFor,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        ArgumentNullException.ThrowIfNull(issuedFor);

        try
        {
            return await redis.GetDatabase()
                .StringSetAsync(KeyFor(ticket), $"{issuedFor.UserId:D}|{issuedFor.SecurityStamp}", lifetime)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(StoreAsync), ex);
            return false;
        }
    }

    public async Task<PasswordResetTicketLookup> FindAsync(string ticket, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return PasswordResetTicketLookup.Invalid;
        }

        try
        {
            return Parse(await redis.GetDatabase().StringGetAsync(KeyFor(ticket)).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(FindAsync), ex);
            return PasswordResetTicketLookup.Unavailable;
        }
    }

    public async Task<PasswordResetTicketLookup> RedeemAsync(string ticket, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return PasswordResetTicketLookup.Invalid;
        }

        try
        {
            var value = await redis.GetDatabase()
                .ScriptEvaluateAsync(RedeemScript, [KeyFor(ticket)])
                .ConfigureAwait(false);

            return Parse((RedisValue)value);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(RedeemAsync), ex);
            return PasswordResetTicketLookup.Unavailable;
        }
    }

    private static PasswordResetTicketLookup Parse(RedisValue value)
    {
        var text = (string?)value;
        var separator = text?.IndexOf('|', StringComparison.Ordinal) ?? -1;

        return separator > 0 && Guid.TryParse(text.AsSpan(0, separator), out var userId)
            ? PasswordResetTicketLookup.Found(new PasswordResetTicket(userId, text![(separator + 1)..]))
            : PasswordResetTicketLookup.Invalid;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Password-reset ticket store unavailable during {Operation}; treating the ticket as unusable.")]
    private static partial void LogStoreUnavailable(ILogger logger, string operation, Exception exception);
}

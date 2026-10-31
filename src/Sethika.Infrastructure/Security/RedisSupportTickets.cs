using System.Globalization;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Support tickets (plan 04, Y1a) as Redis keys that expire by themselves, spent by a read-and-delete
/// script so two switches racing on one ticket cannot both enter.
/// </summary>
/// <remarks>Like the step-up grants, an outage answers "no ticket": entering a tenant is never let through unverified.</remarks>
public sealed partial class RedisSupportTickets(
    IConnectionMultiplexer redis,
    ILogger<RedisSupportTickets> logger) : ISupportTickets
{
    /// <summary>A script rather than GETDEL, which needs Redis 6.2 (see OtpService).</summary>
    private const string SpendScript =
        "local v = redis.call('GET', KEYS[1]) if v then redis.call('DEL', KEYS[1]) end return v";

    /// <summary>The key for one user's ticket into one institution.</summary>
    public static string KeyFor(Guid userId, int institutionId) =>
        string.Create(CultureInfo.InvariantCulture, $"support:{userId:D}:{institutionId}");

    public async Task<bool> IssueAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await redis.GetDatabase()
                .StringSetAsync(KeyFor(userId, institutionId), 1, ISupportTickets.Lifetime)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(IssueAsync), ex);
            return false;
        }
    }

    public async Task<bool> SpendAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await redis.GetDatabase()
                .ScriptEvaluateAsync(SpendScript, [KeyFor(userId, institutionId)])
                .ConfigureAwait(false);

            return !value.IsNull;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(SpendAsync), ex);
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Support ticket store unavailable during {Operation}; treating the ticket as absent.")]
    private static partial void LogStoreUnavailable(ILogger logger, string operation, Exception exception);
}

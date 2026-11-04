using System.Globalization;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Step-up grants as Redis keys that expire by themselves (plan A5).
/// </summary>
/// <remarks>
/// Deliberately unlike the rate limiter, which fails open: a rate limit that cannot count lets a
/// request through that was probably fine, but a step-up that cannot be confirmed would let a
/// sensitive action through unverified. Here an outage answers "not granted".
/// </remarks>
public sealed partial class RedisStepUpGrants(
    IConnectionMultiplexer redis,
    ILogger<RedisStepUpGrants> logger) : IStepUpGrants
{
    /// <summary>The key for one user's grant on one access token.</summary>
    public static string KeyFor(Guid userId, string tokenId) =>
        string.Create(CultureInfo.InvariantCulture, $"stepup:{userId:D}:{tokenId}");

    public async Task<bool> GrantAsync(
        Guid userId,
        string tokenId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        try
        {
            return await redis.GetDatabase()
                .StringSetAsync(KeyFor(userId, tokenId), 1, lifetime)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(GrantAsync), ex);
            return false;
        }
    }

    public async Task<bool> IsGrantedAsync(Guid userId, string tokenId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        try
        {
            return await redis.GetDatabase().KeyExistsAsync(KeyFor(userId, tokenId)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(IsGrantedAsync), ex);
            return false;
        }
    }

    /// <summary>The remaining lifetime of the old key, copied to the new one in one step.</summary>
    private const string CarryScript =
        """
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl <= 0 then return 0 end
        redis.call('SET', KEYS[2], 1, 'PX', ttl)
        return 1
        """;

    public async Task<bool> CarryAsync(Guid userId, string fromTokenId, string toTokenId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromTokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toTokenId);

        try
        {
            var moved = await redis.GetDatabase()
                .ScriptEvaluateAsync(CarryScript, [KeyFor(userId, fromTokenId), KeyFor(userId, toTokenId)])
                .ConfigureAwait(false);
            return (long)moved == 1;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, nameof(CarryAsync), ex);
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Step-up store unavailable during {Operation}; treating the step-up as not granted.")]
    private static partial void LogStoreUnavailable(ILogger logger, string operation, Exception exception);
}

using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>
/// Fixed-window rate limiting in Redis (plan A12).
/// </summary>
/// <remarks>
/// A fixed window allows a burst of up to twice the limit across a window boundary - the limit at
/// the end of one window and again at the start of the next. That is accepted here: the limits exist
/// to make guessing and SMS abuse uneconomic, not to shape traffic, and the account lockout and the
/// per-number OTP cap sit behind them. A sliding-window log in a sorted set is the upgrade, and it
/// would change this class only.
/// </remarks>
public sealed partial class RedisRateLimiter(
    IConnectionMultiplexer redis,
    ILogger<RedisRateLimiter> logger) : IRateLimiter
{
    public async Task<RateLimitDecision> AcquireAsync(
        RateLimitRule rule,
        string subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(subject);

        try
        {
            var (count, windowRemaining) = await RedisWindowCounter
                .IncrementAsync(redis.GetDatabase(), RateLimitKey.For(rule.Name, subject), rule.Window)
                .ConfigureAwait(false);

            return count <= rule.Limit
                ? RateLimitDecision.Allowed
                : RateLimitDecision.Denied(windowRemaining);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogFailingOpen(logger, rule.Name, ex);
            return RateLimitDecision.Allowed;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rate-limit store unavailable; allowing the request under rule {Rule} without counting it.")]
    private static partial void LogFailingOpen(ILogger logger, string rule, Exception exception);
}

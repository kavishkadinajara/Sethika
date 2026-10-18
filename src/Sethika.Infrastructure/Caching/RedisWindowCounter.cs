using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>
/// A counter that expires at the end of a fixed window, incremented and given its expiry in one step.
/// </summary>
/// <remarks>
/// The obvious version - INCR, then EXPIRE when the count is 1 - is two commands. A process that dies
/// between them leaves a counter with no expiry, and whatever it limits is then refused forever: a
/// phone that can never be sent a code, a login that can never be tried. A Lua script runs atomically
/// on the server, so the gap does not exist.
///
/// It also repairs a counter it finds without an expiry, so a key left behind by the old two-command
/// code heals on its next use instead of needing someone to find and delete it.
/// </remarks>
internal static class RedisWindowCounter
{
    private const string IncrementScript =
        """
        local count = redis.call('INCR', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
            ttl = tonumber(ARGV[1])
        end
        return {count, ttl}
        """;

    /// <summary>Increments the counter at <paramref name="key"/>, starting a window if none is open.</summary>
    /// <returns>The count after this increment, and how long the current window has left.</returns>
    public static async Task<(long Count, TimeSpan WindowRemaining)> IncrementAsync(
        IDatabase database,
        string key,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(database);

        var result = (RedisResult[])(await database.ScriptEvaluateAsync(
            IncrementScript,
            [key],
            [(long)window.TotalMilliseconds]).ConfigureAwait(false))!;

        return ((long)result[0], TimeSpan.FromMilliseconds((long)result[1]));
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>Timings of a Redis lock.</summary>
public abstract class LockOptions
{
    /// <summary>
    /// How long a lock lives if its holder never releases it (a crashed process). Longer than any
    /// transaction it guards, so a slow holder is not overtaken while it still works.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a caller waits for the holder before answering "busy".</summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The pause between attempts while waiting.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(20);
}

/// <summary>
/// One Redis lock key: <c>SET NX PX</c> with a random token, released by a script that deletes the
/// key only while it still holds that token. Shared by the slot-day lock (plan 03, C2) and the
/// leader lock (Phase E, V3).
/// </summary>
/// <remarks>
/// The token is what makes the release safe. A holder slower than <see cref="LockOptions.Lifetime"/>
/// loses the lock to the next caller when the key expires; a plain <c>DEL</c> on its way out would
/// then delete the next caller's lock, and a third would walk in beside it.
///
/// Unlike the step-up store, an outage fails open (<see cref="LockOutcome.Unavailable"/>): what these
/// locks guard, the database refuses with or without them.
/// </remarks>
internal sealed partial class RedisLockStore(IConnectionMultiplexer redis, TimeProvider timeProvider, ILogger logger)
{
    private const string ReleaseScript =
        """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end
        return 0
        """;

    public async Task<ILockLease> AcquireAsync(string key, LockOptions settings, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var deadline = timeProvider.GetTimestamp() + (long)(settings.WaitTimeout.TotalSeconds * timeProvider.TimestampFrequency);

        try
        {
            var database = redis.GetDatabase();
            while (true)
            {
                if (await database.StringSetAsync(key, token, settings.Lifetime, When.NotExists).ConfigureAwait(false))
                {
                    return new Lease(this, key, token, LockOutcome.Acquired);
                }

                if (timeProvider.GetTimestamp() >= deadline)
                {
                    LogBusy(logger, key);
                    return new Lease(this, key, token, LockOutcome.Busy);
                }

                await Task.Delay(settings.RetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, key, ex);
            return new Lease(this, key, token, LockOutcome.Unavailable);
        }
    }

    private async Task ReleaseAsync(string key, string token)
    {
        try
        {
            await redis.GetDatabase()
                .ScriptEvaluateAsync(ReleaseScript, [key], [token])
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The key expires by itself; until then the next caller waits, which is safe.
            LogReleaseFailed(logger, key, ex);
        }
    }

    private sealed class Lease(RedisLockStore owner, string key, string token, LockOutcome outcome) : ILockLease
    {
        private int _released;

        public LockOutcome Outcome { get; } = outcome;

        public ValueTask DisposeAsync() =>
            Outcome == LockOutcome.Acquired && Interlocked.Exchange(ref _released, 1) == 0
                ? new ValueTask(owner.ReleaseAsync(key, token))
                : ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lock {Key} stayed busy for the whole wait.")]
    private static partial void LogBusy(ILogger logger, string key);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Lock store unavailable for {Key}; going on without the lock, and the database still refuses what it guards.")]
    private static partial void LogStoreUnavailable(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not release lock {Key}; it expires by itself.")]
    private static partial void LogReleaseFailed(ILogger logger, string key, Exception exception);
}

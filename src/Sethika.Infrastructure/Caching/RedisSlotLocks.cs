using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>Timings of the slot-day lock.</summary>
public sealed class SlotLockOptions
{
    public const string SectionName = "SlotLocks";

    /// <summary>
    /// How long a lock lives if its holder never releases it (a crashed process). Longer than any
    /// booking transaction, so a slow holder is not overtaken while it still works.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a caller waits for the holder before answering "busy".</summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The pause between attempts while waiting.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(20);
}

/// <summary>
/// The slot-day lock as a Redis key, <c>lock:slot:{mealSlotId}:{yyyy-MM-dd}</c> (plan 03, C2):
/// <c>SET NX PX</c> with a random token, released by a script that deletes the key only while it
/// still holds that token.
/// </summary>
/// <remarks>
/// The token is what makes the release safe. A holder slower than <see cref="SlotLockOptions.Lifetime"/>
/// loses the lock to the next caller when the key expires; a plain <c>DEL</c> on its way out would
/// then delete the next caller's lock, and a third would walk in beside it.
///
/// Unlike the step-up store, an outage fails open (<see cref="SlotLockOutcome.Unavailable"/>): the
/// database refuses an overbooking with or without the lock.
/// </remarks>
public sealed partial class RedisSlotLocks(
    IConnectionMultiplexer redis,
    IOptions<SlotLockOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisSlotLocks> logger) : ISlotLocks
{
    private const string ReleaseScript =
        """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end
        return 0
        """;

    /// <summary>The key for one slot on one date.</summary>
    public static string KeyFor(int mealSlotId, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"lock:slot:{mealSlotId}:{date:yyyy-MM-dd}");

    public async Task<ISlotLock> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var key = KeyFor(mealSlotId, slotDate);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var deadline = timeProvider.GetTimestamp() + (long)(settings.WaitTimeout.TotalSeconds * timeProvider.TimestampFrequency);

        try
        {
            var database = redis.GetDatabase();
            while (true)
            {
                if (await database.StringSetAsync(key, token, settings.Lifetime, When.NotExists).ConfigureAwait(false))
                {
                    return new Lease(this, key, token, SlotLockOutcome.Acquired);
                }

                if (timeProvider.GetTimestamp() >= deadline)
                {
                    LogBusy(logger, key);
                    return new Lease(this, key, token, SlotLockOutcome.Busy);
                }

                await Task.Delay(settings.RetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogStoreUnavailable(logger, key, ex);
            return new Lease(this, key, token, SlotLockOutcome.Unavailable);
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

    private sealed class Lease(RedisSlotLocks owner, string key, string token, SlotLockOutcome outcome) : ISlotLock
    {
        private int _released;

        public SlotLockOutcome Outcome { get; } = outcome;

        public ValueTask DisposeAsync() =>
            Outcome == SlotLockOutcome.Acquired && Interlocked.Exchange(ref _released, 1) == 0
                ? new ValueTask(owner.ReleaseAsync(key, token))
                : ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Slot lock {Key} stayed busy for the whole wait.")]
    private static partial void LogBusy(ILogger logger, string key);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Slot lock store unavailable for {Key}; booking goes on without the lock, and the database still refuses an overbooking.")]
    private static partial void LogStoreUnavailable(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not release slot lock {Key}; it expires by itself.")]
    private static partial void LogReleaseFailed(ILogger logger, string key, Exception exception);
}

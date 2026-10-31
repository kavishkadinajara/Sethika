using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>Timings of the slot-day lock.</summary>
public sealed class SlotLockOptions : LockOptions
{
    public const string SectionName = "SlotLocks";
}

/// <summary>
/// The slot-day lock as a Redis key, <c>lock:slot:{mealSlotId}:{yyyy-MM-dd}</c> (plan 03, C2), on
/// <see cref="RedisLockStore"/>.
/// </summary>
/// <remarks>
/// An outage fails open (<see cref="LockOutcome.Unavailable"/>): the database refuses an overbooking
/// with or without the lock.
/// </remarks>
public sealed class RedisSlotLocks(
    IConnectionMultiplexer redis,
    IOptions<SlotLockOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisSlotLocks> logger) : ISlotLocks
{
    private readonly RedisLockStore _store = new(redis, timeProvider, logger);

    /// <summary>The key for one slot on one date.</summary>
    public static string KeyFor(int mealSlotId, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"lock:slot:{mealSlotId}:{date:yyyy-MM-dd}");

    public Task<ILockLease> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default) =>
        _store.AcquireAsync(KeyFor(mealSlotId, slotDate), options.Value, cancellationToken);
}

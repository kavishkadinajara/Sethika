using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>Timings of the leader lock.</summary>
public sealed class LeaderLockOptions : LockOptions
{
    public const string SectionName = "LeaderLocks";
}

/// <summary>
/// Leader locks as Redis keys, <c>lock:leader:{leaderId}</c> (Phase E, V3), on
/// <see cref="RedisLockStore"/>: one per leader, taken in ascending id order.
/// </summary>
/// <remarks>
/// The order is what rules out a deadlock between two administrators: one assigning leaders 3 and 5,
/// the other 5 and 3, both reach for 3 first, so the second waits instead of holding 5. If any key
/// stays busy, the keys already taken are given back at once. If the store is down the lease says
/// so and the caller goes on: the time blocks' primary key refuses an overlap anyway (V2).
/// </remarks>
public sealed class RedisLeaderLocks(
    IConnectionMultiplexer redis,
    IOptions<LeaderLockOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisLeaderLocks> logger) : ILeaderLocks
{
    private readonly RedisLockStore _store = new(redis, timeProvider, logger);

    /// <summary>The key for one leader.</summary>
    public static string KeyFor(int leaderId) =>
        string.Create(CultureInfo.InvariantCulture, $"lock:leader:{leaderId}");

    public async Task<ILockLease> AcquireAsync(IEnumerable<int> leaderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leaderIds);

        var held = new List<ILockLease>();
        foreach (var leaderId in leaderIds.Distinct().Order())
        {
            var lease = await _store.AcquireAsync(KeyFor(leaderId), options.Value, cancellationToken).ConfigureAwait(false);
            if (lease.Outcome != LockOutcome.Acquired)
            {
                await new Leases(held, LockOutcome.Acquired).DisposeAsync().ConfigureAwait(false);
                return new Leases([], lease.Outcome);
            }

            held.Add(lease);
        }

        return new Leases(held, LockOutcome.Acquired);
    }

    /// <summary>Every leader's lease, released together, last taken first.</summary>
    private sealed class Leases(List<ILockLease> held, LockOutcome outcome) : ILockLease
    {
        public LockOutcome Outcome { get; } = outcome;

        public async ValueTask DisposeAsync()
        {
            for (var i = held.Count - 1; i >= 0; i--)
            {
                await held[i].DisposeAsync().ConfigureAwait(false);
            }

            held.Clear();
        }
    }
}

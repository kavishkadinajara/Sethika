namespace Sethika.Application.Abstractions;

/// <summary>
/// Serialises assignments of the same religious leaders across every API instance (Phase E, V3).
/// </summary>
/// <remarks>
/// The first layer, like <see cref="ISlotLocks"/>: a loser gets a quick 409 to try again instead of a
/// key violation. The guarantee is the primary key of <c>booking.LeaderTimeBlocks</c> (V2), so an
/// outage goes on without the lock.
/// </remarks>
public interface ILeaderLocks
{
    /// <summary>
    /// Takes every leader's lock, in ascending id order so that two callers wanting overlapping sets
    /// cannot each hold one the other is waiting for. If any one stays busy, those already taken are
    /// given back and the lease is <see cref="LockOutcome.Busy"/>.
    /// </summary>
    Task<ILockLease> AcquireAsync(IEnumerable<int> leaderIds, CancellationToken cancellationToken = default);
}

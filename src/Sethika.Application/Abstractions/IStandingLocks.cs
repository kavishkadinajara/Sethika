namespace Sethika.Application.Abstractions;

/// <summary>Row locks on a standing sponsorship, held by the context's open transaction until it ends.</summary>
public interface IStandingLocks
{
    /// <summary>
    /// Takes an update lock on sponsorship <paramref name="sponsorshipId"/>'s row and says whether it is
    /// still active, as of the lock (plan 08, K4). Booking a day, letting one go and ending the
    /// sponsorship each take it first, so they happen one at a time even without the slot-day's Redis
    /// lock: a day skipped is never booked by a run that read the day a moment before, and an ended
    /// sponsorship books nothing more. The row itself is not changed, so its <c>RowVersion</c> stays.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task<bool> LockActiveAsync(int sponsorshipId, CancellationToken cancellationToken = default);
}

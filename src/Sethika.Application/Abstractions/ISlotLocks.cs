namespace Sethika.Application.Abstractions;

/// <summary>How an attempt on a slot-day's lock ended.</summary>
public enum SlotLockOutcome
{
    /// <summary>The caller holds the lock until it disposes the <see cref="ISlotLock"/>.</summary>
    Acquired,

    /// <summary>Somebody else held it for the whole wait. The caller answers 409 and the donor tries again.</summary>
    Busy,

    /// <summary>
    /// The lock store could not be reached. The caller goes on without the lock (plan 03, C2): the
    /// conditional UPDATE and the CHECKs are correct on their own, and an outage must not stop
    /// bookings.
    /// </summary>
    Unavailable,
}

/// <summary>One attempt on a slot-day's lock. Disposing it releases the lock if it was taken.</summary>
public interface ISlotLock : IAsyncDisposable
{
    SlotLockOutcome Outcome { get; }
}

/// <summary>
/// Serialises booking attempts on one slot-day across every API instance (plan 03, C2, layer 1).
/// </summary>
/// <remarks>
/// The lock is an optimisation, not the guarantee. It makes the get-or-create of the slot-day and the
/// bookability check run one attempt at a time, so a loser gets a quick, clean answer instead of a
/// constraint error; correctness rests on the database alone.
/// </remarks>
public interface ISlotLocks
{
    /// <summary>
    /// Takes the lock on <paramref name="mealSlotId"/> on <paramref name="slotDate"/>, waiting a short while
    /// for a holder to finish.
    /// </summary>
    Task<ISlotLock> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default);
}

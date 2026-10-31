namespace Sethika.Application.Abstractions;

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
    Task<ILockLease> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default);
}

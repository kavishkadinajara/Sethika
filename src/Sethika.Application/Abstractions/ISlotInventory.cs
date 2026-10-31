using Sethika.Domain.Bookings;

namespace Sethika.Application.Abstractions;

/// <summary>
/// The slot-day counters (plan 03, C2 and C3), inside the caller's transaction. This is where
/// overbooking is made impossible, so it is SQL, not change tracking.
/// </summary>
public interface ISlotInventory
{
    /// <summary>
    /// The slot's instance on <paramref name="slotDate"/>, created with the slot's capacity if it does
    /// not exist yet (C3). Two callers creating it at once both get the one row.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task<SlotInstance> GetOrCreateAsync(MealSlot slot, DateOnly slotDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one sponsor place and <paramref name="headcount"/> people from the instance, if both fit
    /// and it is open. The row stays locked until the caller's transaction ends.
    /// </summary>
    /// <returns>Whether the place was taken; <see langword="false"/> means the slot-day is full.</returns>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task<bool> TryTakeAsync(long slotInstanceId, short headcount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives back the place a rejected or cancelled booking held (C1), reopening a full slot-day.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the context, or there was nothing to give back.</exception>
    Task ReleaseAsync(long slotInstanceId, short headcount, CancellationToken cancellationToken = default);
}

namespace Sethika.Domain.Bookings;

/// <summary>
/// Days an institution takes out of booking: a festival, a closure. Maps <c>booking.BlockedDates</c>.
/// </summary>
public class BlockedDate
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }

    /// <summary>The one slot blocked, or <see langword="null"/> for every slot of the institution.</summary>
    public int? MealSlotId { get; set; }

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string Reason { get; set; } = null!;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Whether this block takes <paramref name="slot"/> out on <paramref name="date"/>.</summary>
    public bool Blocks(MealSlot slot, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return slot.InstitutionId == InstitutionId
               && (MealSlotId is null || MealSlotId == slot.Id)
               && date >= DateFrom && date <= DateTo;
    }
}

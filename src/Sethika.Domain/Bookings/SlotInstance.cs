namespace Sethika.Domain.Bookings;

/// <summary>
/// One slot on one date: the inventory a booking takes a place from. Maps <c>booking.SlotInstances</c>.
/// </summary>
/// <remarks>
/// The counters are never changed through this class. The engine changes them with one conditional
/// UPDATE (C2), whose <c>WHERE</c> and the table's CHECKs are what make overbooking impossible; an
/// in-memory increment saved later would race. They have private setters so nothing tries.
/// </remarks>
public class SlotInstance
{
    public long Id { get; set; }
    public int InstitutionId { get; set; }
    public int MealSlotId { get; set; }
    public DateOnly SlotDate { get; set; }

    /// <summary>Copied from the slot when the instance is created, and never again (C3).</summary>
    public byte MaxSponsors { get; private set; }

    /// <summary>Copied from the slot when the instance is created, and never again (C3).</summary>
    public short MaxHeadcount { get; private set; }

    public byte BookedSponsors { get; private set; }
    public short BookedHeadcount { get; private set; }

    /// <summary><see cref="SlotInstanceStatus"/>.</summary>
    public string Status { get; private set; } = SlotInstanceStatus.Open;

    public byte[] RowVersion { get; set; } = [];

    public int RemainingSponsors => MaxSponsors - BookedSponsors;
    public int RemainingHeadcount => MaxHeadcount - BookedHeadcount;

    /// <summary>The first instance of <paramref name="slot"/> on <paramref name="date"/>: empty, open, its capacity snapshotted.</summary>
    public static SlotInstance Open(MealSlot slot, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new SlotInstance
        {
            InstitutionId = slot.InstitutionId,
            MealSlotId = slot.Id,
            SlotDate = date,
            MaxSponsors = slot.MaxSponsors,
            MaxHeadcount = slot.MaxHeadcount,
        };
    }
}

/// <summary><c>booking.SlotInstances.Status</c>, as <c>CK_SlotInst_Status</c> spells it.</summary>
public static class SlotInstanceStatus
{
    public const string Open = "Open";

    /// <summary>Every sponsor place is taken.</summary>
    public const string Full = "Full";

    /// <summary>In the schema; plan 03 blocks days through <see cref="BlockedDate"/> instead.</summary>
    public const string Blocked = "Blocked";

    /// <summary>In the schema; not used by plan 03.</summary>
    public const string Closed = "Closed";
}

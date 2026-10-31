namespace Sethika.Domain.Bookings;

/// <summary>
/// A meal an institution offers to sponsors: "Lunch, 11:00-12:30, every day, up to 60 people".
/// Maps <c>booking.MealSlots</c>.
/// </summary>
/// <remarks>
/// A slot is the offer, not the inventory. Each date it is booked on gets a
/// <see cref="SlotInstance"/> that snapshots <see cref="MaxSponsors"/> and
/// <see cref="MaxHeadcount"/>, so editing the slot never breaks a promise already made (C3).
/// </remarks>
public class MealSlot
{
    public int Id { get; set; }

    /// <summary>The slot's address in URLs and APIs (C9); <see cref="Id"/> never leaves the server.</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public string Name { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// The days the slot runs, one bit per <see cref="DayOfWeek"/>: Sunday is 1, Monday 2, and so on
    /// to Saturday 64. 127 is every day.
    /// </summary>
    public byte DaysOfWeekMask { get; set; } = EveryDay;

    /// <summary><see cref="Bookings.BookingMode"/>.</summary>
    public string BookingMode { get; set; } = Bookings.BookingMode.Exclusive;

    /// <summary>1 for an exclusive slot; 2-20 for a shared one (<c>CK_MealSlots_Sponsors</c>).</summary>
    public byte MaxSponsors { get; set; } = 1;

    public short MinHeadcount { get; set; } = 1;
    public short MaxHeadcount { get; set; }
    public decimal? EstimatedCostPerHead { get; set; }

    /// <summary>The earliest a booking may be made, in days before the meal. 0 allows the same day.</summary>
    public short MinAdvanceDays { get; set; } = 1;

    /// <summary>The furthest ahead a booking may be made, in days.</summary>
    public short MaxAdvanceDays { get; set; } = 365;

    /// <summary>When off, a booking is approved the moment it is made.</summary>
    public bool RequiresApproval { get; set; } = true;

    /// <summary>The <see cref="FulfilmentMode"/>s offered, comma-separated as the column stores them.</summary>
    public string AllowedFulfilment { get; set; } = FulfilmentMode.DonorBrings + "," + FulfilmentMode.InstitutionPrepares;

    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<MealSlotSeason> Seasons { get; } = new List<MealSlotSeason>();

    /// <summary>All seven days.</summary>
    public const byte EveryDay = 127;

    /// <summary>The bit <paramref name="day"/> has in <see cref="DaysOfWeekMask"/>.</summary>
    public static byte DayBit(DayOfWeek day) => (byte)(1 << (int)day);

    /// <summary>Whether the slot runs on <paramref name="date"/>'s day of the week.</summary>
    public bool RunsOn(DateOnly date) => (DaysOfWeekMask & DayBit(date.DayOfWeek)) != 0;

    /// <summary>Whether a donor may choose <paramref name="fulfilment"/> on this slot.</summary>
    public bool Offers(string fulfilment) =>
        AllowedFulfilment.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(fulfilment, StringComparer.Ordinal);
}

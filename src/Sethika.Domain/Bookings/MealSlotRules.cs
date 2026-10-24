namespace Sethika.Domain.Bookings;

/// <summary>
/// Whether a slot can be booked on a date (plan 03, C4): pure, so it is unit-tested here and the
/// same code answers both the booking engine and the availability calendar.
/// </summary>
/// <remarks>
/// The engine calls it inside the slot-day's lock, on rows it has just read, so a block added a
/// moment ago is seen. Capacity is not checked here: the conditional UPDATE is the only reliable
/// judge of a place left (C2).
/// </remarks>
public static class MealSlotRules
{
    /// <summary><c>org.Institutions.Status</c> of an institution that takes bookings.</summary>
    public const string ActiveInstitution = "Active";

    /// <summary>
    /// The first reason <paramref name="slot"/> cannot be booked on <paramref name="date"/> at all,
    /// whatever the booking - or <see langword="null"/> if it can.
    /// </summary>
    /// <param name="institutionStatus">The slot's institution's <c>Status</c>.</param>
    /// <param name="seasons">The slot's seasons; none means all year.</param>
    /// <param name="blocks">Blocked dates of the slot's institution; any that do not apply are ignored.</param>
    /// <param name="date">The slot-day, a Colombo date.</param>
    /// <param name="utcNow">Now. Counted in Colombo dates and times (<see cref="SriLankaTime"/>).</param>
    public static string? CanBookOn(
        MealSlot slot,
        string institutionStatus,
        IEnumerable<MealSlotSeason> seasons,
        IEnumerable<BlockedDate> blocks,
        DateOnly date,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(blocks);

        if (!slot.IsActive)
        {
            return BookingErrors.SlotInactive;
        }

        if (institutionStatus != ActiveInstitution)
        {
            return BookingErrors.InstitutionNotActive;
        }

        if (!slot.RunsOn(date))
        {
            return BookingErrors.NotOnThisDay;
        }

        var ownSeasons = seasons.Where(s => s.MealSlotId == slot.Id).ToList();
        if (ownSeasons.Count > 0 && !ownSeasons.Any(s => s.Covers(date)))
        {
            return BookingErrors.OutOfSeason;
        }

        if (blocks.Any(b => b.Blocks(slot, date)))
        {
            return BookingErrors.DateBlocked;
        }

        var now = SriLankaTime.FromUtc(utcNow);
        var today = DateOnly.FromDateTime(now);
        var daysAhead = date.DayNumber - today.DayNumber;

        // Same-day booking (MinAdvanceDays = 0) closes when the meal starts.
        if (daysAhead < slot.MinAdvanceDays || (daysAhead == 0 && TimeOnly.FromDateTime(now) >= slot.StartTime))
        {
            return BookingErrors.TooSoon;
        }

        if (daysAhead > slot.MaxAdvanceDays)
        {
            return BookingErrors.TooFar;
        }

        return null;
    }

    /// <summary>
    /// The first reason this booking of <paramref name="slot"/> on <paramref name="date"/> is refused,
    /// or <see langword="null"/> if it may go ahead to the slot-day's counters.
    /// </summary>
    /// <param name="headcount">People the donor will feed.</param>
    /// <param name="fulfilment">A <see cref="FulfilmentMode"/>.</param>
    public static string? CanBook(
        MealSlot slot,
        string institutionStatus,
        IEnumerable<MealSlotSeason> seasons,
        IEnumerable<BlockedDate> blocks,
        DateOnly date,
        DateTime utcNow,
        short headcount,
        string fulfilment)
    {
        var refused = CanBookOn(slot, institutionStatus, seasons, blocks, date, utcNow);
        if (refused is not null)
        {
            return refused;
        }

        if (headcount < slot.MinHeadcount || headcount > slot.MaxHeadcount)
        {
            return BookingErrors.HeadcountOutOfRange;
        }

        return slot.Offers(fulfilment) ? null : BookingErrors.FulfilmentNotOffered;
    }

    /// <summary>
    /// What a donor finds on <paramref name="date"/> before choosing a headcount: why it cannot be
    /// booked, or the places and people left. The availability calendar's answer (plan 03, 5.5).
    /// </summary>
    /// <remarks>
    /// The calendar is a forecast; the engine still decides with <see cref="CanBook"/> and the
    /// conditional UPDATE, so a day shown open can be taken a moment later. A day is shown full when
    /// the UPDATE would refuse every booking: no sponsor place left, the slot-day closed, or fewer
    /// people left than the slot's smallest booking.
    /// </remarks>
    /// <param name="instance">The slot-day, or <see langword="null"/> if nobody has booked it yet (C3).</param>
    public static SlotDayOpenings Openings(
        MealSlot slot,
        string institutionStatus,
        IEnumerable<MealSlotSeason> seasons,
        IEnumerable<BlockedDate> blocks,
        DateOnly date,
        DateTime utcNow,
        SlotInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(slot);

        // An existing slot-day keeps the capacity it was created with, whatever the slot says now (C3).
        var sponsors = Math.Max(0, instance?.RemainingSponsors ?? slot.MaxSponsors);
        var headcount = Math.Max(0, Math.Min(slot.MaxHeadcount, instance?.RemainingHeadcount ?? slot.MaxHeadcount));

        var refused = CanBookOn(slot, institutionStatus, seasons, blocks, date, utcNow);
        if (refused is null
            && (instance is { Status: not SlotInstanceStatus.Open } || sponsors == 0 || headcount < slot.MinHeadcount))
        {
            refused = BookingErrors.SlotFull;
        }

        return new SlotDayOpenings(refused, sponsors, headcount);
    }
}

/// <summary>A slot-day as the availability calendar shows it.</summary>
/// <param name="Refused">Why it cannot be booked (a <see cref="BookingErrors"/> code), or <see langword="null"/>.</param>
/// <param name="Sponsors">Sponsor places left.</param>
/// <param name="Headcount">The most people one booking may bring now: the slot's limit, or what is left if less.</param>
public readonly record struct SlotDayOpenings(string? Refused, int Sponsors, int Headcount)
{
    public bool Bookable => Refused is null;
}

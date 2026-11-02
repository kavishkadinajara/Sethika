namespace Sethika.Domain.Bookings;

/// <summary>
/// One of an institution's own donors gives a meal on a pattern the institution set up for them (plan
/// 08, K2). Maps <c>booking.StandingSponsorships</c>.
/// </summary>
/// <remarks>
/// Each day of the pattern becomes an ordinary <see cref="Booking"/> with <see cref="Booking.StandingSource"/>,
/// made by the engine and approved at once (K3). The pattern arithmetic is here and pure, so the nightly
/// job, the API's next days and the tests all ask the same question: <see cref="OccursOn"/>.
///
/// A day that does not exist in a month (the 31st in April, 29 February in a common year) is skipped,
/// never moved (Q3): the arithmetic only ever looks at days that exist, so it never meets one.
/// </remarks>
public class StandingSponsorship
{
    /// <summary>The most months between two monthly days: 12, once a year (<c>CK_Standing_MonthInterval</c>).</summary>
    public const int MaxMonthInterval = 12;

    public int Id { get; set; }

    /// <summary>The sponsorship's address in APIs (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public int InstitutionDonorId { get; set; }
    public int MealSlotId { get; set; }

    /// <summary>A <see cref="StandingPattern"/>.</summary>
    public string Pattern { get; set; } = StandingPattern.Once;

    /// <summary>Monthly: every this many months, counted from <see cref="StartDate"/>'s month. 1 otherwise.</summary>
    public byte MonthInterval { get; set; } = 1;

    /// <summary>Weekly: bit 0 Sunday ... bit 6 Saturday, as <see cref="DayOfWeek"/> numbers them.</summary>
    public byte? WeekDays { get; set; }

    /// <summary>Monthly: bit 0 the 1st ... bit 30 the 31st.</summary>
    public int? MonthDays { get; set; }

    /// <summary>Yearly: the month.</summary>
    public byte? YearMonth { get; set; }

    /// <summary>Yearly: the day of <see cref="YearMonth"/>.</summary>
    public byte? YearDay { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>The last day it may fall on, or <see langword="null"/> for no end. A <c>Once</c> ends on its day.</summary>
    public DateOnly? EndDate { get; set; }

    public short Headcount { get; set; }

    /// <summary><see cref="Bookings.FulfilmentMode"/>.</summary>
    public string FulfilmentMode { get; set; } = Bookings.FulfilmentMode.DonorBrings;

    public string? InMemoryOf { get; set; }
    public string? Notes { get; set; }

    /// <summary><see cref="ActiveStatus"/> or <see cref="EndedStatus"/>.</summary>
    public string Status { get; private set; } = ActiveStatus;

    public DateTime? EndedAt { get; private set; }
    public Guid? EndedBy { get; private set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public const string ActiveStatus = "Active";
    public const string EndedStatus = "Ended";

    public bool IsActive => Status == ActiveStatus;

    /// <summary>
    /// Whether the pattern gives a meal on <paramref name="date"/>: inside <see cref="StartDate"/> and
    /// <see cref="EndDate"/>, and one of the pattern's days. Whether it is still active is not asked.
    /// </summary>
    public bool OccursOn(DateOnly date)
    {
        if (date < StartDate || (EndDate is { } end && date > end))
        {
            return false;
        }

        return Pattern switch
        {
            StandingPattern.Once => date == StartDate,
            StandingPattern.Daily => true,
            StandingPattern.Weekly => WeekDays is { } week && HasBit(week, (int)date.DayOfWeek),
            StandingPattern.Monthly => MonthDays is { } days && HasBit(days, date.Day - 1) && MonthsFromStart(date) % MonthInterval == 0,
            StandingPattern.Yearly => date.Month == YearMonth && date.Day == YearDay,
            _ => false,
        };
    }

    /// <summary>The pattern's days from <paramref name="from"/> to <paramref name="to"/>, both included, in order.</summary>
    public IEnumerable<DateOnly> DaysBetween(DateOnly from, DateOnly to)
    {
        var first = from > StartDate ? from : StartDate;
        var last = EndDate is { } end && end < to ? end : to;

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (OccursOn(date))
            {
                yield return date;
            }
        }
    }

    /// <summary>
    /// The next <paramref name="count"/> days of the pattern from <paramref name="from"/> on, looking no
    /// further than <paramref name="horizonDays"/> days ahead (a yearly day may be four years away).
    /// </summary>
    public IReadOnlyList<DateOnly> NextDays(DateOnly from, int count, int horizonDays = 366 * 4) =>
        [.. DaysBetween(from, from.AddDays(horizonDays)).Take(count)];

    /// <summary>Active → Ended. The bookings it already made are the caller's to let go.</summary>
    /// <returns>Whether it moved; an ended sponsorship stays as it ended.</returns>
    public bool End(Guid userId, DateTime now)
    {
        if (!IsActive)
        {
            return false;
        }

        Status = EndedStatus;
        EndedAt = now;
        EndedBy = userId;
        UpdatedAt = now;
        UpdatedBy = userId;
        return true;
    }

    private int MonthsFromStart(DateOnly date) => ((date.Year - StartDate.Year) * 12) + date.Month - StartDate.Month;

    private static bool HasBit(int mask, int bit) => ((mask >> bit) & 1) == 1;
}

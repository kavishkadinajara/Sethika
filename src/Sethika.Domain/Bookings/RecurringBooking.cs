namespace Sethika.Domain.Bookings;

/// <summary>
/// A donor's wish to book the same meal on the same day every year (plan 07, H2). Maps
/// <c>booking.RecurringBookings</c>.
/// </summary>
/// <remarks>
/// A promise to try, not a held place. Each year's booking is an ordinary <see cref="Booking"/> made by
/// the engine on the first day the slot takes bookings for that date (H3), so it is approved,
/// cancelled, reminded and counted like any other. <see cref="LastGeneratedYear"/> is the last year
/// tried, booked or not: each year is tried once, and two runs at once cannot both try it, because
/// the year is claimed with a conditional UPDATE in the booking's own transaction.
/// </remarks>
public class RecurringBooking
{
    public int Id { get; set; }

    /// <summary>How the donor's screens and the API name it; <see cref="Id"/> never leaves the server.</summary>
    public Guid PublicId { get; set; }

    public Guid DonorUserId { get; set; }
    public int InstitutionId { get; set; }
    public int MealSlotId { get; set; }
    public int? MemorialPlanId { get; set; }
    public byte RecurMonth { get; set; }
    public byte RecurDay { get; set; }

    /// <summary>The first year the job books; the year it was made in is booked by the donor there and then.</summary>
    public short StartYear { get; set; }

    /// <summary>The last year it books, or <see langword="null"/> for every year.</summary>
    public short? EndYear { get; set; }

    public int? OccasionLookupId { get; set; }
    public string? InMemoryOf { get; set; }
    public short Headcount { get; set; }

    /// <summary><see cref="Bookings.FulfilmentMode"/>, as the first booking had it.</summary>
    public string FulfilmentMode { get; set; } = Bookings.FulfilmentMode.DonorBrings;

    /// <summary>The last year tried, booked or not; <see langword="null"/> before the first.</summary>
    public short? LastGeneratedYear { get; set; }

    /// <summary>False once the donor stops it; the row stays, and so do the bookings it made.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The yearly booking of <paramref name="booking"/>, made on <paramref name="date"/>: the same meal
    /// on the same day from the next year on (H5), for the same memorial plan when it was booked for one (H6).
    /// </summary>
    public static RecurringBooking From(Booking booking, int mealSlotId, DateOnly date, Guid publicId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new RecurringBooking
        {
            PublicId = publicId,
            DonorUserId = booking.DonorUserId ?? throw new ArgumentException("A yearly booking needs the donor's account.", nameof(booking)),
            InstitutionId = booking.InstitutionId,
            MealSlotId = mealSlotId,
            MemorialPlanId = booking.MemorialPlanId,
            RecurMonth = (byte)date.Month,
            RecurDay = (byte)date.Day,
            StartYear = (short)(date.Year + 1),
            InMemoryOf = booking.InMemoryOf,
            Headcount = booking.Headcount,
            FulfilmentMode = booking.FulfilmentMode,
            LastGeneratedYear = (short)date.Year,
            IsActive = true,
            CreatedAt = now,
        };
    }

    /// <summary>The year it is tried next, or <see langword="null"/> when it is stopped or past its last year.</summary>
    public int? NextYear => NextYearOf(StartYear, EndYear, LastGeneratedYear, IsActive);

    /// <inheritdoc cref="NextYear"/>
    public static int? NextYearOf(short startYear, short? endYear, short? lastGeneratedYear, bool isActive)
    {
        if (!isActive)
        {
            return null;
        }

        var next = Math.Max(startYear, (lastGeneratedYear ?? 0) + 1);
        return endYear is { } last && next > last ? null : next;
    }

    /// <summary>
    /// The day it falls on in <paramref name="year"/>. 29 February books 28 February in a common year:
    /// the nearest day that exists, still in the same month.
    /// </summary>
    public static DateOnly OccursIn(byte month, byte day, int year) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <inheritdoc cref="OccursIn(byte, byte, int)"/>
    public DateOnly OccursIn(int year) => OccursIn(RecurMonth, RecurDay, year);

    /// <summary>
    /// Whether the job books <paramref name="occurrence"/> on <paramref name="today"/> (Colombo): from
    /// the first day the slot takes bookings for it, <paramref name="maxAdvanceDays"/> ahead, which is
    /// the day <see cref="MealSlotRules"/> stops calling it <see cref="BookingErrors.TooFar"/> (H3).
    /// </summary>
    public static bool IsDue(DateOnly occurrence, short maxAdvanceDays, DateOnly today) =>
        occurrence.DayNumber - today.DayNumber <= maxAdvanceDays;
}

namespace Sethika.Application.Abstractions;

/// <summary>A standing sponsorship as its screens show it (plan 08, K2): the pattern as lists, not masks.</summary>
/// <param name="WeekDays">Weekly: 0 Sunday ... 6 Saturday; otherwise empty.</param>
/// <param name="MonthDays">Monthly: 1 ... 31; otherwise empty.</param>
/// <param name="MaxAdvanceDays">The slot's booking window: days further ahead are not booked yet.</param>
/// <param name="Version">The <c>RowVersion</c> ending it sends back.</param>
public sealed record SponsorshipView(
    Guid PublicId,
    Guid Donor,
    string DonorName,
    Guid MealSlot,
    string MealSlotName,
    string Pattern,
    int MonthInterval,
    IReadOnlyList<int> WeekDays,
    IReadOnlyList<int> MonthDays,
    int? YearMonth,
    int? YearDay,
    DateOnly StartDate,
    DateOnly? EndDate,
    short Headcount,
    string Fulfilment,
    string? InMemoryOf,
    string? Notes,
    string Status,
    DateTime? EndedAt,
    short MaxAdvanceDays,
    byte[] Version);

/// <summary>One of a sponsorship's bookings, by its slot-day.</summary>
public sealed record SponsorshipBooking(DateOnly Date, string BookingNo, string Status);

/// <summary>One of a sponsorship's days let go or moved (K4).</summary>
public sealed record SponsorshipDayChange(DateOnly Date, string Kind, DateOnly? OtherDate, string? Reason);

/// <summary>A sponsorship with its bookings and exceptions from a day on.</summary>
public sealed record SponsorshipRecord(SponsorshipView Sponsorship, IReadOnlyList<SponsorshipBooking> Bookings, IReadOnlyList<SponsorshipDayChange> Exceptions);

/// <summary>Reads for the institution's standing sponsorships (plan 08, K2).</summary>
public interface IStandingSponsorshipQueries
{
    /// <summary>The institution's sponsorships, or one donor's: active first, newest first.</summary>
    Task<IReadOnlyList<SponsorshipView>> ListAsync(int institutionId, Guid? donor, CancellationToken cancellationToken = default);

    /// <summary>One sponsorship with its bookings and exceptions on <paramref name="from"/> or later, or <see langword="null"/>.</summary>
    Task<SponsorshipRecord?> GetAsync(int institutionId, Guid publicId, DateOnly from, CancellationToken cancellationToken = default);
}

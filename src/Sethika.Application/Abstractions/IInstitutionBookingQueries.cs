namespace Sethika.Application.Abstractions;

/// <summary>A meal slot as the institution's slot screen shows and edits it.</summary>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
/// <param name="AllowedFulfilment">The <c>FulfilmentMode</c>s offered.</param>
/// <param name="Version">The <c>RowVersion</c> an edit sends back, so it cannot overwrite a newer one.</param>
public sealed record InstitutionMealSlotView(
    Guid PublicId,
    string Name,
    string? NameSi,
    string? NameTa,
    string StartTime,
    string EndTime,
    byte DaysOfWeekMask,
    string BookingMode,
    byte MaxSponsors,
    short MinHeadcount,
    short MaxHeadcount,
    decimal? EstimatedCostPerHead,
    short MinAdvanceDays,
    short MaxAdvanceDays,
    bool RequiresApproval,
    IReadOnlyList<string> AllowedFulfilment,
    short SortOrder,
    bool IsActive,
    byte[] Version);

/// <summary>Days taken out of booking.</summary>
/// <param name="Id">Tenant-internal, never shown to donors (plan 03, C9).</param>
/// <param name="MealSlot">The one slot blocked, or <see langword="null"/> for all.</param>
public sealed record BlockedDateView(int Id, DateOnly DateFrom, DateOnly DateTo, Guid? MealSlot, string? MealSlotName, string Reason);

/// <summary>Whether the institution is in the public list, and the address of its own page.</summary>
public sealed record InstitutionListingView(string Slug, bool Listed);

/// <summary>One booking on the institution's calendar.</summary>
/// <param name="DonorPhone">For arranging the meal with the donor.</param>
public sealed record CalendarBookingView(
    string BookingNo,
    string Status,
    string DonorName,
    string DonorPhone,
    short Headcount,
    string FulfilmentMode,
    string? InMemoryOf,
    string? Notes,
    DateTime CreatedAt,
    string? RejectReason,
    string? CancelReason);

/// <summary>A slot on a day that has been booked: its places, and the bookings that took them.</summary>
/// <param name="Status">The slot-day's: <c>Open</c> or <c>Full</c>.</param>
public sealed record SlotDayView(
    DateOnly Date,
    Guid MealSlot,
    string SlotName,
    string? SlotNameSi,
    string? SlotNameTa,
    string StartTime,
    string EndTime,
    byte MaxSponsors,
    byte BookedSponsors,
    short MaxHeadcount,
    short BookedHeadcount,
    string Status,
    IReadOnlyList<CalendarBookingView> Bookings);

/// <summary>
/// Dapper reads for the institution's booking screens (plan 03, 5.4), always of the one institution
/// named, never of whatever Row-Level Security happens to show.
/// </summary>
public interface IInstitutionBookingQueries
{
    /// <summary>Every slot, inactive ones too, in the order the institution sorts them.</summary>
    Task<IReadOnlyList<InstitutionMealSlotView>> ListSlotsAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>Blocks that end on or after <paramref name="from"/>.</summary>
    Task<IReadOnlyList<BlockedDateView>> ListBlockedDatesAsync(int institutionId, DateOnly from, CancellationToken cancellationToken = default);

    Task<InstitutionListingView> GetListingAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The slot-days from <paramref name="first"/> to <paramref name="last"/> that have bookings, each
    /// with its bookings - only those in <paramref name="status"/> if one is given, and then only the
    /// days that have one.
    /// </summary>
    Task<IReadOnlyList<SlotDayView>> GetCalendarAsync(
        int institutionId,
        DateOnly first,
        DateOnly last,
        string? status,
        CancellationToken cancellationToken = default);
}

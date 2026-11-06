using Sethika.Domain.Bookings;

namespace Sethika.Application.Abstractions;

/// <summary>An institution in the public list: who it is and where.</summary>
/// <param name="Type">The institution type's code.</param>
public sealed record PublicInstitutionSummary(
    string Slug,
    string Name,
    string? NameSi,
    string? NameTa,
    string Type,
    string TypeName,
    string? TypeNameSi,
    string? TypeNameTa,
    string District,
    string? DistrictSi,
    string? DistrictTa,
    string City);

/// <summary>A meal slot as a donor reads it, to choose one and fill the booking form.</summary>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
/// <param name="AllowedFulfilment">The <c>FulfilmentMode</c>s offered.</param>
public sealed record PublicMealSlotView(
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
    IReadOnlyList<string> AllowedFulfilment);

/// <summary>A religious service as a donor reads it, to fill the request form (Phase E, 8.5).</summary>
/// <param name="DurationMinutes">How long it takes; the request's end follows from it.</param>
/// <param name="LocationMode">A <c>ServiceLocation</c>: <c>AtInstitution</c>, <c>AtDonorPlace</c> or <c>Either</c>.</param>
/// <remarks>No travel buffer or leader count: those are the institution's planning, not the donor's choice.</remarks>
public sealed record PublicServiceTypeView(
    Guid PublicId,
    string Name,
    string? NameSi,
    string? NameTa,
    string? Description,
    short DurationMinutes,
    string LocationMode,
    short MinAdvanceDays,
    short MaxAdvanceDays);

/// <summary>
/// An institution's public page: what a donor needs to choose it, its active slots and the religious
/// services it takes requests for. Nothing an institution gave only to the reviewer: no registration
/// number, phone, email or internal ids.
/// </summary>
/// <param name="Services">Active service types, and only while the institution's <c>SERVICES</c> module is on (V12).</param>
/// <param name="GivesOnline">It has an enabled merchant, so its page offers "Give online" (plan 09, M10). Set by the endpoint.</param>
public sealed record PublicInstitutionView(
    string Slug,
    string Name,
    string? NameSi,
    string? NameTa,
    string Type,
    string TypeName,
    string? TypeNameSi,
    string? TypeNameTa,
    string District,
    string? DistrictSi,
    string? DistrictTa,
    string City,
    string AddressLine1,
    string? AddressLine2,
    string? Description,
    string? DietaryNotes,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyList<PublicMealSlotView> Slots,
    IReadOnlyList<PublicServiceTypeView> Services,
    bool GivesOnline = false);

/// <summary>
/// What the availability calendar is computed from: one institution's active slots and their
/// seasons, its blocks and its slot-days over a range of dates. Internal ids stay on the server.
/// </summary>
/// <param name="InstitutionStatus"><c>org.Institutions.Status</c>.</param>
public sealed record AvailabilitySource(
    string InstitutionStatus,
    IReadOnlyList<MealSlot> Slots,
    IReadOnlyList<MealSlotSeason> Seasons,
    IReadOnlyList<BlockedDate> Blocks,
    IReadOnlyList<SlotInstance> SlotDays);

/// <summary>One of the signed-in donor's bookings.</summary>
/// <param name="CanCancel">Whether the donor may still cancel it themselves (Q1).</param>
/// <param name="Yearly">The active yearly booking it belongs to (plan 07, H5), or <see langword="null"/>.</param>
/// <param name="Regular">
/// One of the donor's regular days, set up by the institution (plan 08, K3). Only whether it is: the
/// donor's session does not read the sponsorship itself (K16).
/// </param>
public sealed record DonorBookingView(
    string BookingNo,
    string Status,
    DateOnly Date,
    string StartTime,
    string EndTime,
    string Institution,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    Guid MealSlot,
    string SlotName,
    string? SlotNameSi,
    string? SlotNameTa,
    short Headcount,
    string FulfilmentMode,
    string? InMemoryOf,
    string? Notes,
    DateTime CreatedAt,
    string? RejectReason,
    string? CancelReason,
    bool CanCancel,
    Guid? Yearly,
    bool Regular);

/// <summary>One of the signed-in donor's active yearly bookings (plan 07, H5).</summary>
/// <param name="NextDate">The day it books next, or <see langword="null"/> after its last year.</param>
/// <param name="LastYear">The last year the job tried, booked or not; the year it was made in at first.</param>
public sealed record RecurringBookingView(
    Guid PublicId,
    string Institution,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    Guid MealSlot,
    string SlotName,
    string? SlotNameSi,
    string? SlotNameTa,
    string StartTime,
    string EndTime,
    int Month,
    int Day,
    DateOnly? NextDate,
    int? LastYear,
    short Headcount,
    string FulfilmentMode,
    string? InMemoryOf);

/// <summary>
/// Dapper reads for the donor's side of booking (plan 03, 5.5). The institution reads are anonymous
/// and see only institutions that are <c>Active</c>; the list also only those publicly listed.
/// </summary>
public interface IDonorBookingQueries
{
    /// <summary>Active, publicly listed institutions, by name (C10).</summary>
    Task<IReadOnlyList<PublicInstitutionSummary>> ListInstitutionsAsync(CancellationToken cancellationToken = default);

    /// <summary>An active institution by its slug, listed or not, with its active slots and services; <see langword="null"/> if none.</summary>
    Task<PublicInstitutionView?> GetInstitutionAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active institution <paramref name="slug"/>'s slots, seasons, and the blocks and slot-days
    /// touching <paramref name="first"/> to <paramref name="last"/>; <see langword="null"/> if no such institution.
    /// </summary>
    Task<AvailabilitySource?> GetAvailabilitySourceAsync(string slug, DateOnly first, DateOnly last, CancellationToken cancellationToken = default);

    /// <summary>The donor's own bookings, latest slot-day first, at most <see cref="MaxMine"/>.</summary>
    /// <param name="today">Today in Colombo, for <see cref="DonorBookingView.CanCancel"/>.</param>
    Task<IReadOnlyList<DonorBookingView>> ListMineAsync(Guid donorId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>The active institutions among <paramref name="userId"/>'s places (plan 07, H12), the newest kept first.</summary>
    Task<IReadOnlyList<PublicInstitutionSummary>> ListPlacesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The donor's active yearly bookings, soonest next date first.</summary>
    Task<IReadOnlyList<RecurringBookingView>> ListYearlyAsync(Guid donorId, CancellationToken cancellationToken = default);

    /// <summary>The slug of institution <paramref name="institutionId"/>, whatever its status: what its open calendars watch (plan 07, H9).</summary>
    Task<string?> SlugOfAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The most bookings <see cref="ListMineAsync"/> returns; the list has no paging yet.</summary>
    const int MaxMine = 200;
}

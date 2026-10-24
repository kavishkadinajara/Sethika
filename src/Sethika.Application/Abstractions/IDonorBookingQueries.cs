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

/// <summary>
/// An institution's public page: what a donor needs to choose it, and its active slots. Nothing an
/// institution gave only to the reviewer: no registration number, phone, email or internal ids.
/// </summary>
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
    IReadOnlyList<PublicMealSlotView> Slots);

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
    bool CanCancel);

/// <summary>
/// Dapper reads for the donor's side of booking (plan 03, 5.5). The institution reads are anonymous
/// and see only institutions that are <c>Active</c>; the list also only those publicly listed.
/// </summary>
public interface IDonorBookingQueries
{
    /// <summary>Active, publicly listed institutions, by name (C10).</summary>
    Task<IReadOnlyList<PublicInstitutionSummary>> ListInstitutionsAsync(CancellationToken cancellationToken = default);

    /// <summary>An active institution by its slug, listed or not, with its active slots; <see langword="null"/> if none.</summary>
    Task<PublicInstitutionView?> GetInstitutionAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active institution <paramref name="slug"/>'s slots, seasons, and the blocks and slot-days
    /// touching <paramref name="first"/> to <paramref name="last"/>; <see langword="null"/> if no such institution.
    /// </summary>
    Task<AvailabilitySource?> GetAvailabilitySourceAsync(string slug, DateOnly first, DateOnly last, CancellationToken cancellationToken = default);

    /// <summary>The donor's own bookings, latest slot-day first, at most <see cref="MaxMine"/>.</summary>
    /// <param name="today">Today in Colombo, for <see cref="DonorBookingView.CanCancel"/>.</param>
    Task<IReadOnlyList<DonorBookingView>> ListMineAsync(Guid donorId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>The most bookings <see cref="ListMineAsync"/> returns; the list has no paging yet.</summary>
    const int MaxMine = 200;
}

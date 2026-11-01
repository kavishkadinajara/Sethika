using Sethika.Domain.Bookings;
using Sethika.Domain.Institutions;

namespace Sethika.Application.Abstractions;

/// <summary>Which institutions a search reads (plan 06, G1): every filter applies; none means every listed institution.</summary>
/// <param name="InstitutionIds">Only these, the geo index's candidates; <see langword="null"/> for no such limit.</param>
/// <param name="Within">Only those within <paramref name="WithinKm"/> of this point, measured by SQL Server: the fallback when the index cannot answer.</param>
/// <param name="Type">An <c>org.InstitutionTypes.Code</c>.</param>
/// <param name="District">A <c>core.Districts.Code</c>.</param>
public sealed record SearchSourceFilter(
    IReadOnlyCollection<int>? InstitutionIds,
    GeoPoint? Within,
    double? WithinKm,
    string? Type,
    string? District);

/// <summary>A candidate institution: what a result shows, and where it is. Its id stays on the server.</summary>
public sealed record SearchInstitution(
    int Id,
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
    string Status,
    decimal Latitude,
    decimal Longitude);

/// <summary>
/// What a search is judged from: the candidate institutions, their active slots and seasons, and the
/// blocks and slot-days touching the searched dates.
/// </summary>
/// <param name="TypeKnown">The type asked for exists, or none was asked for.</param>
/// <param name="DistrictKnown">The district asked for exists, or none was asked for.</param>
public sealed record SearchSource(
    bool TypeKnown,
    bool DistrictKnown,
    IReadOnlyList<SearchInstitution> Institutions,
    IReadOnlyList<MealSlot> Slots,
    IReadOnlyList<MealSlotSeason> Seasons,
    IReadOnlyList<BlockedDate> Blocks,
    IReadOnlyList<SlotInstance> SlotDays);

/// <summary>One search, as the demand log keeps it (G8): no user and no coordinates.</summary>
public sealed record SearchLogEntry(DateOnly TargetDate, string? Type, string? District, int? RadiusKm, int ResultCount);

/// <summary>Dapper reads for the cross-institution search (plan 06). Anonymous, like the public institution reads.</summary>
public interface IDiscoveryQueries
{
    /// <summary>
    /// The institutions that are <c>Active</c>, publicly listed and pass <paramref name="filter"/>, with
    /// everything needed to judge their slots from <paramref name="first"/> to <paramref name="last"/> (G7).
    /// </summary>
    Task<SearchSource> GetSearchSourceAsync(SearchSourceFilter filter, DateOnly first, DateOnly last, CancellationToken cancellationToken = default);

    /// <summary>Adds a row to <c>booking.AvailabilitySearches</c>.</summary>
    Task LogSearchAsync(SearchLogEntry entry, CancellationToken cancellationToken = default);
}

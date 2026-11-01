using System.Data;
using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the cross-institution search (plan 06, B.1).</summary>
/// <remarks>
/// Anonymous, like <see cref="DonorBookingQueries"/>' institution reads: institutions, slots, seasons,
/// blocks and slot-days are public under Row-Level Security (plan 03, C8), so what a search shows is
/// decided here, by the <c>Active</c> and listed filter and the columns chosen. The filter holds on
/// every path: the geo index's candidates are filtered again, since the index can lag the database
/// by an outbox run.
/// </remarks>
public sealed class DiscoveryQueries(IDbConnectionFactory connections) : IDiscoveryQueries
{
    /// <summary><c>org.InstitutionTypes.Code</c>'s length.</summary>
    private const int TypeLength = 30;

    /// <summary><c>core.Districts.Code</c>'s length.</summary>
    private const int DistrictLength = 10;

    // One batch: the candidates go into a table variable once, and every later read joins it.
    // OPTION (RECOMPILE) plans the candidate query for the filters actually given, so the spatial
    // index serves a search near a point and the district index a search in a district, instead of
    // one plan for every combination of "@x IS NULL OR ...".
    private const string SourceSql =
        """
        DECLARE @typeId SMALLINT = (SELECT t.Id FROM org.InstitutionTypes AS t WHERE t.Code = @type);
        DECLARE @districtId SMALLINT = (SELECT d.Id FROM core.Districts AS d WHERE d.Code = @district);

        SELECT CAST(IIF(@type IS NULL OR @typeId IS NOT NULL, 1, 0) AS BIT) AS TypeKnown,
               CAST(IIF(@district IS NULL OR @districtId IS NOT NULL, 1, 0) AS BIT) AS DistrictKnown;

        DECLARE @candidates TABLE (Id INT NOT NULL PRIMARY KEY);

        INSERT @candidates (Id)
        SELECT i.Id
        FROM org.Institutions AS i
        WHERE i.Status = 'Active' AND i.IsPubliclyListed = 1 AND i.IsDeleted = 0
          AND (@type IS NULL OR i.InstitutionTypeId = @typeId)
          AND (@district IS NULL OR i.DistrictId = @districtId)
          AND (@byIds = 0 OR i.Id IN (SELECT c.Id FROM @ids AS c))
          AND (@withinMetres IS NULL
               OR i.Location.STDistance(geography::Point(@latitude, @longitude, 4326)) <= @withinMetres)
        OPTION (RECOMPILE);

        SELECT i.Id AS Id, i.Slug AS Slug, i.Name AS Name, i.NameSi AS NameSi, i.NameTa AS NameTa,
               t.Code AS Type, t.Name AS TypeName, t.NameSi AS TypeNameSi, t.NameTa AS TypeNameTa,
               d.Name AS District, d.NameSi AS DistrictSi, d.NameTa AS DistrictTa, i.City AS City,
               i.Status AS Status, i.Latitude AS Latitude, i.Longitude AS Longitude
        FROM @candidates AS c
        JOIN org.Institutions AS i ON i.Id = c.Id
        JOIN org.InstitutionTypes AS t ON t.Id = i.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = i.DistrictId;

        SELECT s.Id AS Id, s.PublicId AS PublicId, s.InstitutionId AS InstitutionId, s.Name AS Name,
               s.NameSi AS NameSi, s.NameTa AS NameTa, s.StartTime AS StartTime, s.EndTime AS EndTime,
               s.DaysOfWeekMask AS DaysOfWeekMask, s.BookingMode AS BookingMode, s.MaxSponsors AS MaxSponsors,
               s.MinHeadcount AS MinHeadcount, s.MaxHeadcount AS MaxHeadcount, s.MinAdvanceDays AS MinAdvanceDays,
               s.MaxAdvanceDays AS MaxAdvanceDays, s.RequiresApproval AS RequiresApproval,
               s.AllowedFulfilment AS AllowedFulfilment, s.IsActive AS IsActive
        FROM booking.MealSlots AS s
        JOIN @candidates AS c ON c.Id = s.InstitutionId
        WHERE s.IsActive = 1
        ORDER BY s.InstitutionId, s.SortOrder, s.StartTime, s.Name;

        SELECT ss.Id AS Id, ss.MealSlotId AS MealSlotId, ss.Name AS Name, ss.DateFrom AS DateFrom, ss.DateTo AS DateTo
        FROM booking.MealSlotSeasons AS ss
        JOIN booking.MealSlots AS s ON s.Id = ss.MealSlotId
        JOIN @candidates AS c ON c.Id = s.InstitutionId
        WHERE s.IsActive = 1;

        SELECT b.Id AS Id, b.InstitutionId AS InstitutionId, b.MealSlotId AS MealSlotId,
               b.DateFrom AS DateFrom, b.DateTo AS DateTo, b.Reason AS Reason
        FROM booking.BlockedDates AS b
        JOIN @candidates AS c ON c.Id = b.InstitutionId
        WHERE b.DateFrom <= @last AND b.DateTo >= @first;

        SELECT si.Id AS Id, si.InstitutionId AS InstitutionId, si.MealSlotId AS MealSlotId, si.SlotDate AS SlotDate,
               si.MaxSponsors AS MaxSponsors, si.MaxHeadcount AS MaxHeadcount,
               si.BookedSponsors AS BookedSponsors, si.BookedHeadcount AS BookedHeadcount, si.Status AS Status
        FROM booking.SlotInstances AS si
        JOIN @candidates AS c ON c.Id = si.InstitutionId
        WHERE si.SlotDate >= @first AND si.SlotDate <= @last;
        """;

    // Retired types are left out: no listed institution can be one, so filtering by it finds nobody.
    private const string LookupsSql =
        """
        SELECT t.Code AS Code, t.Name AS Name, t.NameSi AS NameSi, t.NameTa AS NameTa, t.Category AS Category
        FROM org.InstitutionTypes AS t
        WHERE t.IsActive = 1
        ORDER BY t.Name;

        SELECT d.Code AS Code, d.Name AS Name, d.NameSi AS NameSi, d.NameTa AS NameTa
        FROM core.Districts AS d
        ORDER BY d.Name;
        """;

    // No user and no coordinates (G8): only what was asked for and how many answered.
    private const string LogSql =
        """
        INSERT booking.AvailabilitySearches (TargetDate, InstitutionTypeId, DistrictId, RadiusKm, ResultCount)
        SELECT @date,
               (SELECT t.Id FROM org.InstitutionTypes AS t WHERE t.Code = @type),
               (SELECT d.Id FROM core.Districts AS d WHERE d.Code = @district),
               @radiusKm, @resultCount;
        """;

    public async Task<SearchSource> GetSearchSourceAsync(SearchSourceFilter filter, DateOnly first, DateOnly last, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var ids = new DataTable();
        ids.Columns.Add("Id", typeof(int));
        foreach (var id in filter.InstitutionIds ?? [])
        {
            ids.Rows.Add(id);
        }

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            SourceSql,
            new
            {
                type = Code(filter.Type, TypeLength),
                district = Code(filter.District, DistrictLength),
                byIds = filter.InstitutionIds is not null,
                ids = ids.AsTableValuedParameter("core.IntList"),
                withinMetres = filter is { Within: not null, WithinKm: { } km } ? km * 1000 : (double?)null,

                // Unused unless withinMetres is set; geography::Point needs numbers either way.
                latitude = filter.Within?.Latitude ?? 0,
                longitude = filter.Within?.Longitude ?? 0,
                first,
                last,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var known = await reader.ReadSingleAsync<KnownRow>().ConfigureAwait(false);
        var institutions = await reader.ReadAsync<SearchInstitution>().ConfigureAwait(false);
        var slots = await reader.ReadAsync<SlotRuleRow>().ConfigureAwait(false);
        var seasons = await reader.ReadAsync<MealSlotSeason>().ConfigureAwait(false);
        var blocks = await reader.ReadAsync<BlockedDate>().ConfigureAwait(false);

        // Dapper sets the counters through their private setters; nothing here changes them (C2).
        var slotDays = await reader.ReadAsync<SlotInstance>().ConfigureAwait(false);

        return new SearchSource(
            known.TypeKnown,
            known.DistrictKnown,
            [.. institutions],
            [.. slots.Select(r => r.ToMealSlot())],
            [.. seasons],
            [.. blocks],
            [.. slotDays]);
    }

    public async Task<SearchLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(LookupsSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var types = await reader.ReadAsync<InstitutionTypeOption>().ConfigureAwait(false);
        var districts = await reader.ReadAsync<DistrictOption>().ConfigureAwait(false);
        return new SearchLookups([.. types], [.. districts]);
    }

    public async Task LogSearchAsync(SearchLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            LogSql,
            new
            {
                date = entry.TargetDate,
                type = Code(entry.Type, TypeLength),
                district = Code(entry.District, DistrictLength),
                radiusKm = (short?)entry.RadiusKm,

                // ResultCount is a SMALLINT; a count beyond it is still "plenty".
                resultCount = (short)Math.Min(entry.ResultCount, short.MaxValue),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>A code as its <c>VARCHAR</c> column holds it, so the unique index is seeked, not converted.</summary>
    private static DbString Code(string? code, int length) => new() { Value = code, IsAnsi = true, Length = length };

    private sealed class KnownRow
    {
        public bool TypeKnown { get; init; }
        public bool DistrictKnown { get; init; }
    }
}

using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Institutions;

namespace Sethika.Application.Bookings.Discovery;

/// <summary>What a donor narrows a search by (plan 06, G1). Where is a point and a radius, a district, or neither: anywhere.</summary>
/// <param name="TimeOfDay">A <see cref="MealTimes"/> value.</param>
/// <param name="Type">An institution type's code.</param>
/// <param name="Near">The point to search around: the donor's position or the map's centre (G3).</param>
/// <param name="RadiusKm">One of <see cref="AvailabilitySearch.RadiiKm"/>; <see cref="AvailabilitySearch.DefaultRadiusKm"/> when a point comes without one.</param>
/// <param name="District">A district's code.</param>
public sealed record SearchFilters(string TimeOfDay, string? Type, GeoPoint? Near, int? RadiusKm, string? District);

/// <summary>A slot open on the searched day, and what is left of it.</summary>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
/// <param name="Sponsors">Sponsor places left.</param>
/// <param name="Headcount">The most people one booking may bring now.</param>
public sealed record SearchSlotView(
    Guid MealSlot,
    string Name,
    string? NameSi,
    string? NameTa,
    string StartTime,
    string EndTime,
    int Sponsors,
    int Headcount);

/// <summary>An institution with at least one slot open on the searched day: what its public page shows of it.</summary>
/// <param name="DistanceKm">From the searched point, to 10 m; <see langword="null"/> when no point was given.</param>
public sealed record SearchResultView(
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
    decimal Latitude,
    decimal Longitude,
    double? DistanceKm,
    IReadOnlyList<SearchSlotView> Slots);

/// <summary>A search's answer.</summary>
/// <param name="Date"><c>yyyy-MM-dd</c>.</param>
/// <param name="More">More institutions matched than <see cref="AvailabilitySearch.MaxResults"/>; the nearest, or the first by name, are shown.</param>
public sealed record SearchView(string Date, IReadOnlyList<SearchResultView> Results, bool More);

/// <summary>A search's answer, or the filters that cannot be searched.</summary>
public sealed record SearchResult(SearchView? View, IReadOnlyList<string> Fields)
{
    /// <summary>Some filters are missing, malformed or contradict each other; <see cref="Fields"/> names them.</summary>
    public const string InvalidSearch = "invalid_search";

    public bool Succeeded => View is not null;

    internal static SearchResult Ok(SearchView view) => new(view, []);

    internal static SearchResult Invalid(IReadOnlyList<string> fields) => new(null, fields);
}

/// <summary>
/// The cross-institution search (plan 06, B.1): "which institutions near Galle have a free lunch slot
/// on 14 March?" Every institution that is <c>Active</c>, listed and passes the filters, with the
/// slots it can be booked for that day.
/// </summary>
/// <remarks>
/// <para>
/// Near a point, the geo index names the candidates (G4); if it cannot answer, SQL Server measures
/// instead (G5). Either way the candidates come from a slightly wider circle and
/// <see cref="GeoPoint.DistanceKmTo"/> decides who is inside and in what order, so an outage changes
/// how fast the answer comes, never the answer.
/// </para>
/// <para>
/// Every slot-day is judged by <see cref="MealSlotRules.Openings"/>, like the institution's own
/// calendar and the engine (G7), from rows read for this search: nothing is cached, so a slot shown
/// open was open when the search ran.
/// </para>
/// </remarks>
public sealed class AvailabilitySearch(IDiscoveryQueries queries, IInstitutionGeoIndex geoIndex, TimeProvider timeProvider)
{
    /// <summary>The most institutions one answer lists.</summary>
    public const int MaxResults = 100;

    /// <summary>The radius of a search near a point that names none.</summary>
    public const int DefaultRadiusKm = 10;

    /// <summary><c>org.InstitutionTypes.Code</c>'s length.</summary>
    private const int MaxTypeLength = 30;

    /// <summary><c>core.Districts.Code</c>'s length.</summary>
    private const int MaxDistrictLength = 10;

    /// <summary>
    /// How much wider than the searched circle candidates are fetched. Redis and SQL Server each
    /// measure on their own model of the earth, up to about half a percent from
    /// <see cref="GeoPoint"/>'s sphere; with this margin, every institution <see cref="GeoPoint"/> puts
    /// inside the circle is among the candidates of both.
    /// </summary>
    private const double CandidateMargin = 1.02;

    /// <summary>The radii a donor may choose, in km.</summary>
    public static IReadOnlyList<int> RadiiKm { get; } = [5, 10, 25, 50];

    /// <summary>The institutions with a slot open on <paramref name="date"/>, a Colombo date, nearest first or by name.</summary>
    public async Task<SearchResult> SearchAsync(DateOnly date, SearchFilters filters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invalid = new List<string>();
        if (date < SriLankaTime.Today(now))
        {
            invalid.Add("date");
        }

        invalid.AddRange(Check(filters));
        if (invalid.Count > 0)
        {
            return SearchResult.Invalid(invalid);
        }

        var radiusKm = filters.Near is null ? (int?)null : filters.RadiusKm ?? DefaultRadiusKm;
        var source = await SourceAsync(filters, radiusKm, date, date, cancellationToken).ConfigureAwait(false);
        if (Unknown(source) is { Count: > 0 } unknown)
        {
            return SearchResult.Invalid(unknown);
        }

        var judge = new SlotDayJudge(source, now);
        var matches = Candidates(source, filters.Near, radiusKm)
            .Select(c => (c.Institution, c.DistanceKm, Slots: judge.Open(c.Institution, date, filters.TimeOfDay)))
            .Where(m => m.Slots.Count > 0)
            .ToList();

        await queries.LogSearchAsync(
            new SearchLogEntry(date, filters.Type, filters.District, radiusKm, matches.Count),
            cancellationToken).ConfigureAwait(false);

        return SearchResult.Ok(new SearchView(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. matches.Take(MaxResults).Select(m => View(m.Institution, m.DistanceKm, m.Slots))],
            matches.Count > MaxResults));
    }

    /// <summary>The filters that cannot be searched as given, before anything is read.</summary>
    private static List<string> Check(SearchFilters filters)
    {
        var invalid = new List<string>();
        if (!MealTimes.All.Contains(filters.TimeOfDay, StringComparer.Ordinal))
        {
            invalid.Add("time");
        }

        if (filters.Type is { Length: 0 or > MaxTypeLength })
        {
            invalid.Add("type");
        }

        if (filters.Near is { InSriLanka: false })
        {
            invalid.Add("near");
        }

        // A radius means nothing without a point to draw it around.
        if (filters.RadiusKm is { } radius && (filters.Near is null || !RadiiKm.Contains(radius)))
        {
            invalid.Add("radiusKm");
        }

        // Near a point or in a district, not both: the two could only contradict each other.
        if (filters.District is { } district && (filters.Near is not null || district.Length is 0 or > MaxDistrictLength))
        {
            invalid.Add("district");
        }

        return invalid;
    }

    /// <summary>A type or district code that names nothing.</summary>
    private static List<string> Unknown(SearchSource source)
    {
        var unknown = new List<string>();
        if (!source.TypeKnown)
        {
            unknown.Add("type");
        }

        if (!source.DistrictKnown)
        {
            unknown.Add("district");
        }

        return unknown;
    }

    /// <summary>The candidates and their slots: from the geo index's circle if it answers, else SQL Server's.</summary>
    private async Task<SearchSource> SourceAsync(SearchFilters filters, int? radiusKm, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        if (filters.Near is not { } centre)
        {
            return await queries.GetSearchSourceAsync(
                new SearchSourceFilter(null, null, null, filters.Type, filters.District),
                first,
                last,
                cancellationToken).ConfigureAwait(false);
        }

        var withinKm = radiusKm!.Value * CandidateMargin;
        var ids = await geoIndex.WithinAsync(centre, withinKm, cancellationToken).ConfigureAwait(false);

        return await queries.GetSearchSourceAsync(
            ids is null
                ? new SearchSourceFilter(null, centre, withinKm, filters.Type, null)
                : new SearchSourceFilter(ids, null, null, filters.Type, null),
            first,
            last,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The candidates inside the circle, nearest first, or all of them by name when no point was
    /// given. Ties go by name, then slug, so both paths list the same institutions in the same order.
    /// </summary>
    private static List<(SearchInstitution Institution, double? DistanceKm)> Candidates(SearchSource source, GeoPoint? near, int? radiusKm)
    {
        var candidates = source.Institutions
            .Select(i => (Institution: i, DistanceKm: near?.DistanceKmTo(new GeoPoint((double)i.Latitude, (double)i.Longitude))));

        if (near is not null)
        {
            candidates = candidates.Where(c => c.DistanceKm <= radiusKm);
        }

        return [.. candidates
            .OrderBy(c => c.DistanceKm ?? 0)
            .ThenBy(c => c.Institution.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Institution.Slug, StringComparer.Ordinal)];
    }

    private static SearchResultView View(SearchInstitution i, double? distanceKm, IReadOnlyList<SearchSlotView> slots) => new(
        i.Slug, i.Name, i.NameSi, i.NameTa, i.Type, i.TypeName, i.TypeNameSi, i.TypeNameTa, i.District, i.DistrictSi,
        i.DistrictTa, i.City, i.Latitude, i.Longitude, distanceKm is { } km ? Math.Round(km, 2) : null, slots);

    /// <summary>The source indexed for judging slot-days: each institution's slots and blocks, each slot's seasons, each slot-day.</summary>
    private sealed class SlotDayJudge(SearchSource source, DateTime utcNow)
    {
        private readonly ILookup<int, MealSlot> _slots = source.Slots.ToLookup(s => s.InstitutionId);
        private readonly ILookup<int, MealSlotSeason> _seasons = source.Seasons.ToLookup(s => s.MealSlotId);
        private readonly ILookup<int, BlockedDate> _blocks = source.Blocks.ToLookup(b => b.InstitutionId);
        private readonly Dictionary<(int, DateOnly), SlotInstance> _slotDays = source.SlotDays.ToDictionary(d => (d.MealSlotId, d.SlotDate));

        /// <summary><paramref name="institution"/>'s slots of <paramref name="timeOfDay"/> that can be booked on <paramref name="date"/>, in its own order.</summary>
        public List<SearchSlotView> Open(SearchInstitution institution, DateOnly date, string timeOfDay) =>
        [
            .. _slots[institution.Id]
                .Where(slot => MealTimes.Matches(timeOfDay, slot.StartTime))
                .Select(slot => (Slot: slot, Openings: MealSlotRules.Openings(
                    slot,
                    institution.Status,
                    _seasons[slot.Id],
                    _blocks[institution.Id],
                    date,
                    utcNow,
                    _slotDays.GetValueOrDefault((slot.Id, date)))))
                .Where(s => s.Openings.Bookable)
                .Select(s => new SearchSlotView(
                    s.Slot.PublicId,
                    s.Slot.Name,
                    s.Slot.NameSi,
                    s.Slot.NameTa,
                    s.Slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    s.Slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    s.Openings.Sponsors,
                    s.Openings.Headcount)),
        ];
    }
}

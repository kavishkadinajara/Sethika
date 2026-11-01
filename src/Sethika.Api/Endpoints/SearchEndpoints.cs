using System.Globalization;
using Sethika.Api.RateLimiting;
using Sethika.Application.Bookings.Discovery;
using Sethika.Domain.Bookings;
using Sethika.Domain.Institutions;

namespace Sethika.Api.Endpoints;

/// <summary>The cross-institution search (plan 06): which institutions have a slot open on a day, near a point or in a district.</summary>
/// <remarks>
/// Anonymous and limited per client address, like the institution reads it leads to (G8): searching
/// must not need an account. The searched point is used and forgotten; the search log keeps no
/// coordinates and no user.
/// </remarks>
internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var search = app.MapGroup("/api/search").AllowAnonymous();

        search.MapGet("/", SearchAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);

        return app;
    }

    /// <summary><c>?date=yyyy-MM-dd&amp;time=midday&amp;type=TEMPLE&amp;near=6.927,79.861&amp;radiusKm=10</c>, or <c>&amp;district=CMB</c> instead of the point.</summary>
    private static async Task<IResult> SearchAsync(
        string? date,
        string? time,
        string? type,
        string? near,
        string? radiusKm,
        string? district,
        AvailabilitySearch search,
        CancellationToken cancellationToken)
    {
        var invalid = new List<string>();
        if (!DateOnly.TryParseExact(date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            invalid.Add("date");
        }

        GeoPoint? point = null;
        if (!string.IsNullOrWhiteSpace(near))
        {
            if (TryParsePoint(near, out var parsed))
            {
                point = parsed;
            }
            else
            {
                invalid.Add("near");
            }
        }

        int? radius = null;
        if (!string.IsNullOrWhiteSpace(radiusKm))
        {
            if (int.TryParse(radiusKm.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var km))
            {
                radius = km;
            }
            else
            {
                invalid.Add("radiusKm");
            }
        }

        if (invalid.Count > 0)
        {
            return Invalid(invalid);
        }

        var result = await search.SearchAsync(
            day,
            new SearchFilters(
                string.IsNullOrWhiteSpace(time) ? MealTimes.Any : time.Trim().ToLowerInvariant(),
                Blank(type),
                point,
                radius,
                Blank(district)),
            cancellationToken);

        return result.Succeeded ? Results.Ok(result.View) : Invalid(result.Fields);
    }

    private static IResult Invalid(IReadOnlyList<string> fields) =>
        Results.Json(new { code = SearchResult.InvalidSearch, fields }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary><c>lat,lng</c> in degrees.</summary>
    private static bool TryParsePoint(string text, out GeoPoint point)
    {
        point = default;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var longitude))
        {
            return false;
        }

        point = new GeoPoint(latitude, longitude);
        return true;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

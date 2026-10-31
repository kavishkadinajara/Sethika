using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Discovery;

/// <summary>One slot-day on the donor's calendar.</summary>
/// <param name="Reason">Why it cannot be booked (a <see cref="BookingErrors"/> code), or <see langword="null"/>.</param>
/// <param name="Sponsors">Sponsor places left.</param>
/// <param name="Headcount">The most people one booking may bring now.</param>
public sealed record DayAvailabilityView(DateOnly Date, bool Bookable, string? Reason, int Sponsors, int Headcount);

/// <summary>A slot's days in the month.</summary>
public sealed record SlotAvailabilityView(Guid MealSlot, IReadOnlyList<DayAvailabilityView> Days);

/// <summary>A month of an institution's availability.</summary>
/// <param name="Month"><c>yyyy-MM</c>.</param>
public sealed record AvailabilityView(string Month, IReadOnlyList<SlotAvailabilityView> Slots);

/// <summary>
/// A month of an institution's availability (plan 03, 5.5): each active slot on each day of the
/// month, bookable or the reason it is not, and what is left.
/// </summary>
/// <remarks>
/// Every day is judged by <see cref="MealSlotRules.Openings"/>, which asks
/// <see cref="MealSlotRules.CanBookOn"/> - the engine's own rule - so the calendar and the engine
/// cannot disagree about anything but a booking made in between. Days in the past are refused as
/// <c>too_soon</c>, like any other day too close to book.
/// </remarks>
public sealed class AvailabilityCalendar(IDonorBookingQueries queries, TimeProvider timeProvider)
{
    /// <summary>The month of the institution <paramref name="slug"/>; <see langword="null"/> if there is no such active institution.</summary>
    /// <param name="month">Any day of the month; only its year and month are used.</param>
    public async Task<AvailabilityView?> GetAsync(string slug, DateOnly month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var source = await queries.GetAvailabilitySourceAsync(slug, first, last, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var slotDays = source.SlotDays.ToDictionary(d => (d.MealSlotId, d.SlotDate));

        var slots = source.Slots
            .Select(slot => new SlotAvailabilityView(
                slot.PublicId,
                [.. Days(first, last).Select(date =>
                {
                    var openings = MealSlotRules.Openings(
                        slot,
                        source.InstitutionStatus,
                        source.Seasons,
                        source.Blocks,
                        date,
                        now,
                        slotDays.GetValueOrDefault((slot.Id, date)));

                    return new DayAvailabilityView(date, openings.Bookable, openings.Refused, openings.Sponsors, openings.Headcount);
                })]))
            .ToList();

        return new AvailabilityView(first.ToString("yyyy-MM", CultureInfo.InvariantCulture), slots);
    }

    private static IEnumerable<DateOnly> Days(DateOnly first, DateOnly last)
    {
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}

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
public sealed class AvailabilityCalendar(IDonorBookingQueries queries, IStandingHolds holds, TimeProvider timeProvider)
{
    /// <summary>The month of the institution <paramref name="slug"/>; <see langword="null"/> if there is no such active institution.</summary>
    /// <param name="month">Any day of the month; only its year and month are used.</param>
    public async Task<AvailabilityView?> GetAsync(string slug, DateOnly month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var judge = await JudgeAsync(slug, first, last, cancellationToken).ConfigureAwait(false);
        if (judge is null)
        {
            return null;
        }

        var slots = judge.Source.Slots
            .Select(slot => new SlotAvailabilityView(slot.PublicId, [.. Days(first, last).Select(date => judge.Day(slot, date))]))
            .ToList();

        return new AvailabilityView(first.ToString("yyyy-MM", CultureInfo.InvariantCulture), slots);
    }

    /// <summary>
    /// Chosen days of one active slot, judged exactly as its month is: for a standing sponsorship's
    /// preview (plan 08, K9). <see langword="null"/> if there is no such active institution or slot.
    /// </summary>
    public async Task<IReadOnlyList<DayAvailabilityView>?> DaysAsync(string slug, Guid mealSlot, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(dates);
        if (dates.Count == 0)
        {
            return [];
        }

        var judge = await JudgeAsync(slug, dates.Min(), dates.Max(), cancellationToken).ConfigureAwait(false);
        var slot = judge?.Source.Slots.SingleOrDefault(s => s.PublicId == mealSlot);
        return judge is null || slot is null ? null : [.. dates.Select(date => judge.Day(slot, date))];
    }

    private async Task<Judge?> JudgeAsync(string slug, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var source = await queries.GetAvailabilitySourceAsync(slug, first, last, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        // A standing booking is on a slot-day, so a range with none has nothing held.
        var held = source.SlotDays.Count == 0
            ? []
            : (await holds.ListAsync(source.SlotDays[0].InstitutionId, first, last, cancellationToken).ConfigureAwait(false))
                .ToDictionary(h => (h.MealSlotId, h.Date));

        return new Judge(source, source.SlotDays.ToDictionary(d => (d.MealSlotId, d.SlotDate)), held, timeProvider.GetUtcNow().UtcDateTime);
    }

    private static IEnumerable<DateOnly> Days(DateOnly first, DateOnly last)
    {
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    /// <summary>One read of the institution's days, and the rule that judges each.</summary>
    private sealed record Judge(
        AvailabilitySource Source,
        Dictionary<(int MealSlotId, DateOnly Date), SlotInstance> SlotDays,
        Dictionary<(int MealSlotId, DateOnly Date), StandingHold> Held,
        DateTime Now)
    {
        public DayAvailabilityView Day(MealSlot slot, DateOnly date)
        {
            var instance = SlotDays.GetValueOrDefault((slot.Id, date));
            var openings = MealSlotRules.Openings(slot, Source.InstitutionStatus, Source.Seasons, Source.Blocks, date, Now, instance);

            // Full only through a regular donor's hold: the page offers to ask for it (K6).
            var reason = Held.TryGetValue((slot.Id, date), out var hold)
                         && MealSlotRules.IsHeld(slot, openings, instance, hold.Sponsors, hold.Headcount)
                ? BookingErrors.Held
                : openings.Refused;

            return new DayAvailabilityView(date, openings.Bookable, reason, openings.Sponsors, openings.Headcount);
        }
    }
}

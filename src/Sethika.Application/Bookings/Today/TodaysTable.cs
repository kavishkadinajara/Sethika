using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Today;

/// <summary>How a slot sits on today's table.</summary>
public static class TableSlotState
{
    /// <summary>Places left for another sponsor.</summary>
    public const string Open = "Open";

    /// <summary>Every sponsor place taken, or the slot-day closed to more.</summary>
    public const string Full = "Full";

    /// <summary>Taken out by a blocked date: nobody gives that meal today.</summary>
    public const string Closed = "Closed";
}

/// <summary>One meal on today's table.</summary>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
/// <param name="State">A <see cref="TableSlotState"/>.</param>
/// <param name="Requested">Bookings for it still waiting for an answer.</param>
public sealed record TableSlotView(
    Guid MealSlot,
    string Name,
    string? NameSi,
    string? NameTa,
    string StartTime,
    string EndTime,
    byte MaxSponsors,
    byte BookedSponsors,
    int Requested,
    string State);

/// <summary>Today in Colombo and its meals, by start time.</summary>
public sealed record TodaysTableView(DateOnly Date, IReadOnlyList<TableSlotView> Slots);

/// <summary>
/// The institution's meals today (plan 05, R2): every slot laid today, whether anyone booked it or
/// not, with the places taken and the requests to answer. The sidebar draws it as bowls, and the
/// dashboard's timeline (R4) reads the same view.
/// </summary>
/// <remarks>
/// A slot is on the table when <see cref="MealSlotRules.Serves"/> says so, the rule the booking
/// engine checks too, so the table never shows a meal the engine would call off-day or out of
/// season. A blocked slot stays on the table as <see cref="TableSlotState.Closed"/>: a block is the
/// institution's own decision, and the person reading the table should see it. The places come
/// from the slot-day once there is one, because it snapshots them (C3).
/// </remarks>
public sealed class TodaysTable(IInstitutionBookingQueries queries, TimeProvider timeProvider)
{
    public async Task<TodaysTableView> GetAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        var source = await queries.GetTableSourceAsync(institutionId, today, cancellationToken).ConfigureAwait(false);
        var slotDays = source.SlotDays.ToDictionary(d => d.MealSlotId);

        var slots = source.Slots
            .Where(slot => MealSlotRules.Serves(slot, source.Seasons, today))
            .OrderBy(slot => slot.StartTime)
            .ThenBy(slot => slot.SortOrder)
            .ThenBy(slot => slot.Name, StringComparer.Ordinal)
            .Select(slot => Lay(slot, slotDays.GetValueOrDefault(slot.Id), source.Blocks.Any(b => b.Blocks(slot, today))))
            .ToList();

        return new TodaysTableView(today, slots);
    }

    private static TableSlotView Lay(MealSlot slot, TableSlotDay? day, bool blocked)
    {
        var max = day?.MaxSponsors ?? slot.MaxSponsors;
        var booked = day?.BookedSponsors ?? 0;

        var state = blocked || day?.Status is SlotInstanceStatus.Blocked or SlotInstanceStatus.Closed
            ? TableSlotState.Closed
            : day?.Status == SlotInstanceStatus.Full || booked >= max
                ? TableSlotState.Full
                : TableSlotState.Open;

        return new TableSlotView(
            slot.PublicId,
            slot.Name,
            slot.NameSi,
            slot.NameTa,
            slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            max,
            booked,
            day?.Requested ?? 0,
            state);
    }
}

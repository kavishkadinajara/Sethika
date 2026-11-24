using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Recurring;

/// <summary>One run of the yearly job: the day it ran, the years it found due, and what became of them.</summary>
/// <param name="Skipped">Tried by another run, already held by the donor, past, or without a phone.</param>
/// <param name="Busy">The slot-day's lock was held; nothing was claimed, and the next run tries again.</param>
public sealed record RecurringRun(DateOnly Today, int Due, int Booked, int NotBooked, int Skipped, int Busy);

/// <summary>
/// The yearly job (plan 07, H3): every active yearly booking whose next year has entered its slot's
/// booking window today is booked through the engine, one at a time.
/// </summary>
/// <remarks>
/// A year is due from the first day <see cref="MealSlotRules"/> would take the booking, so a yearly
/// booking reaches the slot on the day it opens, before the date can fill. Each year is claimed in
/// its booking's transaction (<see cref="BookMealHandler.BookYearAsync"/>), so a second run, a retry
/// or a run on another instance books nothing twice. Runs as the platform.
/// </remarks>
public sealed class RecurringBookingRun(ISethikaDbContext db, BookMealHandler engine, TimeProvider timeProvider)
{
    public async Task<RecurringRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);

        var candidates = await db.RecurringBookings
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Join(
                db.MealSlots,
                r => r.MealSlotId,
                s => s.Id,
                (r, s) => new { r.Id, r.RecurMonth, r.RecurDay, r.StartYear, r.EndYear, r.LastGeneratedYear, s.MaxAdvanceDays })
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int due = 0, booked = 0, notBooked = 0, skipped = 0, busy = 0;
        foreach (var candidate in candidates)
        {
            if (RecurringBooking.NextYearOf(candidate.StartYear, candidate.EndYear, candidate.LastGeneratedYear, isActive: true) is not { } year
                || !RecurringBooking.IsDue(RecurringBooking.OccursIn(candidate.RecurMonth, candidate.RecurDay, year), candidate.MaxAdvanceDays, today))
            {
                continue;
            }

            due++;
            var result = await engine.BookYearAsync(candidate.Id, year, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case RecurringYearOutcome.Booked:
                    booked++;
                    break;
                case RecurringYearOutcome.NotBooked:
                    notBooked++;
                    break;
                case RecurringYearOutcome.Busy:
                    busy++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        return new RecurringRun(today, due, booked, notBooked, skipped, busy);
    }
}

using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>One run of the standing job: the day it ran and what became of the days it tried.</summary>
/// <param name="Sponsorships">Active sponsorships looked at.</param>
/// <param name="Due">Days in their slots' booking windows with neither a booking nor an exception.</param>
/// <param name="NotBooked">Refused by the engine (full, blocked, closed); tried again next run.</param>
/// <param name="Skipped">Done in the meantime, held by the donor already, or the sponsorship ended.</param>
/// <param name="Busy">The slot-day's lock was held; tried again next run.</param>
public sealed record StandingRun(DateOnly Today, int Sponsorships, int Due, int Booked, int NotBooked, int Skipped, int Busy);

/// <summary>
/// The standing job (plan 08, K3): every active sponsorship's days from today to the end of its slot's
/// booking window (<see cref="MealSlot.MaxAdvanceDays"/>) are booked through the engine, one at a time.
/// </summary>
/// <remarks>
/// Each night the window moves one day further, so each day is booked on the day the slot opens it,
/// before anyone else can take it: a standing day holds the place (Q5). A day already booked or let go
/// is passed over, so a rerun or a second instance books nothing twice (<see cref="StandingDayBooker"/>
/// checks again under the sponsorship's lock). Runs as the platform.
/// </remarks>
public sealed class StandingBookingRun(ISethikaDbContext db, StandingDayBooker booker, TimeProvider timeProvider)
{
    public async Task<StandingRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);

        var candidates = await db.StandingSponsorships
            .AsNoTracking()
            .Where(s => s.Status == StandingSponsorship.ActiveStatus && (s.EndDate == null || s.EndDate >= today))
            .Join(db.MealSlots, s => s.MealSlotId, m => m.Id, (s, m) => new { Sponsorship = s, m.MaxAdvanceDays })
            .OrderBy(c => c.Sponsorship.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int due = 0, booked = 0, notBooked = 0, skipped = 0, busy = 0;
        foreach (var candidate in candidates)
        {
            var days = await booker
                .DaysLeftAsync(candidate.Sponsorship, today, today.AddDays(candidate.MaxAdvanceDays), cancellationToken)
                .ConfigureAwait(false);

            foreach (var day in days)
            {
                due++;
                var result = await booker.BookDayAsync(candidate.Sponsorship.Id, day, cancellationToken).ConfigureAwait(false);
                switch (result.Outcome)
                {
                    case StandingDayOutcome.Booked:
                        booked++;
                        break;
                    case StandingDayOutcome.NotBooked:
                        notBooked++;
                        break;
                    case StandingDayOutcome.Busy:
                        busy++;
                        break;
                    default:
                        skipped++;
                        break;
                }

                if (result.Outcome == StandingDayOutcome.Ended)
                {
                    // Ended while the run was on it: its other days would say the same.
                    break;
                }
            }
        }

        return new StandingRun(today, candidates.Count, due, booked, notBooked, skipped, busy);
    }
}

using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>
/// The institution's other use of a day (plan 08, K7): a blocked date over standing days lets them go
/// as <see cref="StandingDayChange.Blocked"/>, and each sponsor is texted once for the block.
/// </summary>
/// <remarks>
/// <para>
/// Runs inside the transaction that adds the block. Each sponsorship the block touches is taken under
/// its row lock (<see cref="IStandingLocks"/>, K19), in id order so two blocks never wait on each other
/// in a circle. A day of its pattern from today on is let go whether or not it is booked yet: a day
/// beyond the booking window is recorded too, so the nightly job never books it, and lifting the block
/// later does not bring back days the sponsor was told are gone. Today counts: the block says the
/// institution cannot have the meal today.
/// </para>
/// <para>
/// Only standing days. Ordinary donors' bookings on the blocked days stay, and the block's answer names
/// them, as before (Q4).
/// </para>
/// </remarks>
public sealed class StandingBlocks(ISethikaDbContext db, IStandingLocks standingLocks, ISlotInventory inventory)
{
    /// <summary>
    /// Lets go every standing day of the institution's slot <paramref name="mealSlotId"/> (all slots when
    /// <see langword="null"/>) from <paramref name="from"/> to <paramref name="to"/>, today or later.
    /// Saves; the caller's transaction commits.
    /// </summary>
    /// <param name="blockId">The block, which makes each sponsor's text one of a kind.</param>
    /// <returns>How many standing days were let go.</returns>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    public async Task<int> LetGoAsync(
        int institutionId,
        Guid userId,
        int blockId,
        int? mealSlotId,
        DateOnly from,
        DateOnly to,
        string reason,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var first = SriLankaTime.Today(now) is var today && today > from ? today : from;
        if (first > to)
        {
            return 0;
        }

        var sponsorships = await db.StandingSponsorships
            .AsNoTracking()
            .Where(s => s.InstitutionId == institutionId && s.Status == StandingSponsorship.ActiveStatus
                        && (mealSlotId == null || s.MealSlotId == mealSlotId)
                        && s.StartDate <= to && (s.EndDate == null || s.EndDate >= first))
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var cancelled = new List<Booking>();
        var letGo = 0;
        foreach (var sponsorship in sponsorships)
        {
            if (!await standingLocks.LockActiveAsync(sponsorship.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var days = await LetGoDaysAsync(sponsorship, userId, first, to, reason, now, cancelled, cancellationToken).ConfigureAwait(false);
            letGo += days.Count;
            if (days.Count > 0)
            {
                db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.Blocked, sponsorship.PublicId, days[0], $"block-{blockId}", now, last: days[^1]));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After the save, as every change that gives a place back.
        foreach (var booking in cancelled)
        {
            await inventory.ReleaseAsync(booking.SlotInstanceId, booking.Headcount, cancellationToken).ConfigureAwait(false);
        }

        return letGo;
    }

    /// <summary>
    /// One sponsorship's days in the block, under its row lock: each booking cancelled and each day
    /// recorded. A day that has its one change already keeps it, except a swapped-in day
    /// (<see cref="StandingDayChange.MovedFrom"/>), whose booking is cancelled and whose change becomes the block.
    /// </summary>
    private async Task<List<DateOnly>> LetGoDaysAsync(
        StandingSponsorship sponsorship,
        Guid userId,
        DateOnly first,
        DateOnly to,
        string reason,
        DateTime now,
        List<Booking> cancelled,
        CancellationToken cancellationToken)
    {
        var bookings = (await db.Bookings
                .Where(b => b.StandingSponsorshipId == sponsorship.Id
                            && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
                .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { Booking = b, i.SlotDate })
                .Where(d => d.SlotDate >= first && d.SlotDate <= to)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(d => d.SlotDate, d => d.Booking);

        var changes = await db.StandingExceptions
            .Where(e => e.StandingSponsorshipId == sponsorship.Id && e.ExceptionDate >= first && e.ExceptionDate <= to)
            .ToDictionaryAsync(e => e.ExceptionDate, cancellationToken)
            .ConfigureAwait(false);

        var letGo = new List<DateOnly>();
        foreach (var day in sponsorship.DaysBetween(first, to).Union(bookings.Keys).Order())
        {
            var booking = bookings.GetValueOrDefault(day);
            if (changes.TryGetValue(day, out var change))
            {
                if (change.Kind != StandingDayChange.MovedFrom || booking is null)
                {
                    continue;
                }

                change.Kind = StandingDayChange.Blocked;
                change.OtherDate = null;
                change.BookingId = booking.Id;
                change.Reason = reason;
            }
            else
            {
                db.StandingExceptions.Add(new StandingDayChange
                {
                    InstitutionId = sponsorship.InstitutionId,
                    StandingSponsorshipId = sponsorship.Id,
                    ExceptionDate = day,
                    Kind = StandingDayChange.Blocked,
                    BookingId = booking?.Id,
                    Reason = reason,
                    CreatedBy = userId,
                    CreatedAt = now,
                });
            }

            if (booking is not null)
            {
                booking.CancelAsInstitution(reason, userId, now);
                cancelled.Add(booking);
            }

            letGo.Add(day);
        }

        return letGo;
    }
}

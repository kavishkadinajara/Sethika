using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Engine;

/// <summary>What became of one day of a standing sponsorship (plan 08, K3).</summary>
public enum StandingDayOutcome
{
    /// <summary>The day's booking was made, approved.</summary>
    Booked,

    /// <summary>The engine refused the day (full, blocked, closed, too soon); the next run tries again.</summary>
    NotBooked,

    /// <summary>The day already has a booking of this sponsorship, or was let go (K4): nothing to do, ever.</summary>
    AlreadyDone,

    /// <summary>The donor already holds the slot-day through another booking.</summary>
    AlreadyHeld,

    /// <summary>The sponsorship has ended.</summary>
    Ended,

    /// <summary>Another booking of the slot-day held the lock; the next run tries again.</summary>
    Busy,
}

/// <summary>One day of a standing sponsorship: its outcome, the engine's refusal, and the booking made.</summary>
/// <param name="Error">A <c>BookingErrors</c> code when <see cref="StandingDayOutcome.NotBooked"/>.</param>
public sealed record StandingDayResult(DateOnly Date, StandingDayOutcome Outcome, string? Error, string? BookingNo)
{
    internal static StandingDayResult Of(DateOnly date, StandingDayOutcome outcome) => new(date, outcome, null, null);
}

/// <summary>
/// The engine's door for a standing sponsorship's days (plan 08, K3): the same lock, rules, conditional
/// UPDATE and numbering as a donor's booking, so a standing day can never take a place the engine would
/// refuse anyone, but approved at once and booked for the institution's own donor.
/// </summary>
/// <remarks>
/// <para>
/// In this order: the slot-day's Redis lock; one transaction that takes the sponsorship's row lock
/// (<see cref="IStandingLocks"/>) and reads its status under it; checks the day has no booking of the
/// sponsorship in any status and no exception; asks <see cref="MealSlotRules"/>; takes the place; and
/// saves the booking with the hint to open calendars. A refused day writes nothing and is tried again
/// by the next run, so a day that opens later (a block lifted) is still booked.
/// </para>
/// <para>
/// A day booked once is never booked again by the job, whatever became of the booking: a standing day
/// the institution cancelled on its calendar stays cancelled. Letting a day go is recorded as an
/// exception (K4) for the same reason.
/// </para>
/// <para>
/// No text per day: a daily sponsor would get one for every day of the window. The day-before
/// reminder reaches a linked sponsor as for any booking; the sponsorship's own texts are K10's.
/// </para>
/// </remarks>
public sealed class StandingDayBooker(
    ISethikaDbContext db,
    ISlotLocks locks,
    IStandingLocks standingLocks,
    ISlotInventory inventory,
    INumberSeries numbers,
    TimeProvider timeProvider)
{
    private const string DonorPerSlotIndex = "UX_Bookings_DonorPerSlot";

    /// <summary>
    /// Books <paramref name="date"/>, a day of sponsorship <paramref name="sponsorshipId"/>'s pattern,
    /// unless it was booked or let go before. Runs as the institution or as the platform.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="date"/> is not a day of the pattern.</exception>
    public async Task<StandingDayResult> BookDayAsync(int sponsorshipId, DateOnly date, CancellationToken cancellationToken = default)
    {
        try
        {
            return await BookDayCoreAsync(sponsorshipId, date, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // One context serves a whole run: nothing from this day may reach the next one's save.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The days of <paramref name="sponsorship"/> from <paramref name="from"/> to <paramref name="to"/>
    /// that have neither a booking of it nor an exception: the ones a run still has to try. A shortcut
    /// that saves a lock and a transaction per day already done; <see cref="BookDayAsync"/> checks again.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> DaysLeftAsync(StandingSponsorship sponsorship, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sponsorship);

        var booked = await db.Bookings
            .Where(b => b.StandingSponsorshipId == sponsorship.Id)
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => i.SlotDate)
            .Where(d => d >= from && d <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var excepted = await db.StandingExceptions
            .Where(e => e.StandingSponsorshipId == sponsorship.Id && e.ExceptionDate >= from && e.ExceptionDate <= to)
            .Select(e => e.ExceptionDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var done = booked.Concat(excepted).ToHashSet();
        return [.. sponsorship.DaysBetween(from, to).Where(d => !done.Contains(d))];
    }

    private async Task<StandingDayResult> BookDayCoreAsync(int sponsorshipId, DateOnly date, CancellationToken cancellationToken)
    {
        var sponsorship = await db.StandingSponsorships
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sponsorshipId, cancellationToken)
            .ConfigureAwait(false);

        if (sponsorship is not { IsActive: true })
        {
            return StandingDayResult.Of(date, StandingDayOutcome.Ended);
        }

        if (!sponsorship.OccursOn(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "Not a day of the sponsorship's pattern.");
        }

        var donor = await db.InstitutionDonors
            .AsNoTracking()
            .Where(d => d.Id == sponsorship.InstitutionDonorId)
            .Select(d => new { d.FullName, d.Phone, d.UserId })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var slotLock = await locks.AcquireAsync(sponsorship.MealSlotId, date, cancellationToken).ConfigureAwait(false);
        if (slotLock.Outcome == LockOutcome.Busy)
        {
            return StandingDayResult.Of(date, StandingDayOutcome.Busy);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Under the row lock: an end or a skip that got there first has committed by now.
        if (!await standingLocks.LockActiveAsync(sponsorship.Id, cancellationToken).ConfigureAwait(false))
        {
            return StandingDayResult.Of(date, StandingDayOutcome.Ended);
        }

        var excepted = await db.StandingExceptions
            .AnyAsync(e => e.StandingSponsorshipId == sponsorship.Id && e.ExceptionDate == date, cancellationToken)
            .ConfigureAwait(false);

        var booked = await db.Bookings
            .Where(b => b.StandingSponsorshipId == sponsorship.Id)
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => i.SlotDate)
            .AnyAsync(d => d == date, cancellationToken)
            .ConfigureAwait(false);

        if (excepted || booked)
        {
            return StandingDayResult.Of(date, StandingDayOutcome.AlreadyDone);
        }

        var (slot, refused) = await BookMealHandler
            .CheckAsync(db, sponsorship.MealSlotId, date, now, sponsorship.Headcount, sponsorship.FulfilmentMode, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return new StandingDayResult(date, StandingDayOutcome.NotBooked, refused, null);
        }

        var instance = await inventory.GetOrCreateAsync(slot, date, cancellationToken).ConfigureAwait(false);

        // The same person holding the day already (a booking of their own, another sponsorship of
        // theirs): no second place for them. UX_Bookings_DonorPerSlot would refuse it anyway.
        var held = await db.Bookings
            .AnyAsync(
                b => b.SlotInstanceId == instance.Id && b.DonorPhone == donor.Phone
                     && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved),
                cancellationToken)
            .ConfigureAwait(false);

        if (held)
        {
            return StandingDayResult.Of(date, StandingDayOutcome.AlreadyHeld);
        }

        if (!await inventory.TryTakeAsync(instance.Id, sponsorship.Headcount, cancellationToken).ConfigureAwait(false))
        {
            return new StandingDayResult(date, StandingDayOutcome.NotBooked, BookingErrors.SlotFull, null);
        }

        var bookingNo = await numbers
            .NextAsync(NumberSeriesDefinition.Booking, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        db.Bookings.Add(Booking.Standing(bookingNo, sponsorship, instance.Id, donor.UserId, donor.FullName, donor.Phone, sponsorship.CreatedBy, now));
        db.OutboxMessages.Add(AvailabilityChange.Message(sponsorship.InstitutionId, date, date, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == DonorPerSlotIndex)
        {
            // The donor took the day between the check above and this save; disposing the
            // transaction gives the place back.
            return StandingDayResult.Of(date, StandingDayOutcome.AlreadyHeld);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StandingDayResult(date, StandingDayOutcome.Booked, null, bookingNo);
    }
}

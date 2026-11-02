using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>One side of a swap: a sponsorship and one of its days.</summary>
public sealed record SwapDay(Guid? Sponsorship, DateOnly? Date);

/// <summary>Two standing days to swap, this time only or from now on (plan 08, K8).</summary>
public sealed record SwapInput(SwapDay? A, SwapDay? B, bool Permanent);

/// <summary>
/// Two regular donors swap days (plan 08, K8): A has the 5th, B the 12th; this month B gives on the 5th
/// and A on the 12th - once, or for good.
/// </summary>
/// <remarks>
/// <para>
/// Both in one transaction, so a swap never leaves a day empty or held twice: the two slot-days' Redis
/// locks in date order, then both sponsorships' row locks in id order (<see cref="IStandingLocks"/>,
/// K19), and everything else read under them. Two staff swapping the same day at once queue on the
/// first row lock; the second then finds the day changed and is refused.
/// </para>
/// <para>
/// <b>This time</b>: both days must be booked. The two bookings are cancelled and each sponsor booked on
/// the other's day through the engine's rules and conditional UPDATE, approved; the four days are
/// recorded (<see cref="StandingDayChange.MovedTo"/> on each one's own day, <see cref="StandingDayChange.MovedFrom"/>
/// on the day it gives instead), so the job never books either sponsor back.
/// </para>
/// <para>
/// <b>From now on</b>: A ends the day before its day and a new sponsorship gives A's meal on B's pattern
/// from B's day; B the same the other way. Starting each on one of the other's own days keeps an
/// every-N-months pattern in its months. Bookings already made from those days on move to the new
/// sponsorships in the same transaction; the job books the rest. Refused when either has a day let go
/// or moved from its day on: that day's change would have no owner.
/// </para>
/// </remarks>
public sealed class StandingSwapHandler(
    ISethikaDbContext db,
    ISlotLocks locks,
    IStandingLocks standingLocks,
    ISlotInventory inventory,
    INumberSeries numbers,
    TimeProvider timeProvider)
{
    /// <summary>A swapped booking's cancel reason.</summary>
    public const string SwappedReason = "Swapped with another regular donor's day.";

    /// <summary>One side's day is not booked (yet): a swap this time moves two bookings.</summary>
    public const string DayNotBooked = "day_not_booked";

    private const string ExceptionDayIndex = "UQ_StandingExc_Day";
    private const string DonorPerSlotIndex = "UX_Bookings_DonorPerSlot";

    /// <summary>Swaps the two days; the result names the new sponsorships when the swap is for good.</summary>
    public async Task<SwapResult> SwapAsync(int institutionId, Guid userId, SwapInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = SriLankaTime.Today(now);
        var invalid = new List<string>();
        if (input.A is not { Sponsorship: not null, Date: { } dayA } || dayA < today)
        {
            invalid.Add("a");
        }

        if (input.B is not { Sponsorship: not null, Date: { } dayB } || dayB < today
            || input.B.Sponsorship == input.A?.Sponsorship || input.B.Date == input.A?.Date)
        {
            invalid.Add("b");
        }

        if (invalid.Count > 0)
        {
            return SwapResult.Invalid(invalid);
        }

        var a = await FindAsync(institutionId, input.A!.Sponsorship!.Value, cancellationToken).ConfigureAwait(false);
        var b = await FindAsync(institutionId, input.B!.Sponsorship!.Value, cancellationToken).ConfigureAwait(false);
        if (a is null || b is null)
        {
            return SwapResult.Failed(SponsorshipChangeResult.NotFound);
        }

        if (!a.IsActive || !b.IsActive)
        {
            return SwapResult.Failed(SponsorshipChangeResult.Ended);
        }

        var (dateA, dateB) = (input.A.Date!.Value, input.B.Date!.Value);

        // The same meal, and each side's day is one the other does not give already.
        if (b.MealSlotId != a.MealSlotId || a.OccursOn(dateB) || b.OccursOn(dateA))
        {
            return SwapResult.Invalid(["b"]);
        }

        if (input.Permanent && (!a.OccursOn(dateA) || a.Pattern == StandingPattern.Once))
        {
            return SwapResult.Invalid(["a"]);
        }

        if (input.Permanent && (!b.OccursOn(dateB) || b.Pattern == StandingPattern.Once))
        {
            return SwapResult.Invalid(["b"]);
        }

        var (early, late) = dateA < dateB ? (dateA, dateB) : (dateB, dateA);
        await using var earlyLock = await locks.AcquireAsync(a.MealSlotId, early, cancellationToken).ConfigureAwait(false);
        if (earlyLock.Outcome == LockOutcome.Busy)
        {
            return SwapResult.Failed(BookMealResult.SlotBusy);
        }

        await using var lateLock = await locks.AcquireAsync(a.MealSlotId, late, cancellationToken).ConfigureAwait(false);
        if (lateLock.Outcome == LockOutcome.Busy)
        {
            return SwapResult.Failed(BookMealResult.SlotBusy);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var id in new[] { a.Id, b.Id }.Order())
        {
            if (!await standingLocks.LockActiveAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return SwapResult.Failed(SponsorshipChangeResult.Ended);
            }
        }

        var result = input.Permanent
            ? await ForGoodAsync(a, dateA, b, dateB, userId, now, cancellationToken).ConfigureAwait(false)
            : await ThisTimeAsync(a, dateA, b, dateB, userId, now, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Disposing the transaction puts every day back as it was.
            return result;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == ExceptionDayIndex)
        {
            return SwapResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<SwapResult> ThisTimeAsync(StandingSponsorship a, DateOnly dateA, StandingSponsorship b, DateOnly dateB, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        int[] ids = [a.Id, b.Id];
        DateOnly[] dates = [dateA, dateB];
        if (await db.StandingExceptions.AnyAsync(e => ids.Contains(e.StandingSponsorshipId) && dates.Contains(e.ExceptionDate), cancellationToken).ConfigureAwait(false))
        {
            return SwapResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }

        var bookingA = await ActiveBookingAsync(a.Id, dateA, cancellationToken).ConfigureAwait(false);
        var bookingB = await ActiveBookingAsync(b.Id, dateB, cancellationToken).ConfigureAwait(false);
        if (bookingA is null || bookingB is null)
        {
            return SwapResult.Failed(DayNotBooked);
        }

        bookingA.CancelAsInstitution(SwappedReason, userId, now);
        bookingB.CancelAsInstitution(SwappedReason, userId, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await inventory.ReleaseAsync(bookingA.SlotInstanceId, bookingA.Headcount, cancellationToken).ConfigureAwait(false);
        await inventory.ReleaseAsync(bookingB.SlotInstanceId, bookingB.Headcount, cancellationToken).ConfigureAwait(false);

        var (movedA, refusedA) = await BookAsync(a, dateB, now, cancellationToken).ConfigureAwait(false);
        if (refusedA is not null)
        {
            return SwapResult.Failed(refusedA);
        }

        var (movedB, refusedB) = await BookAsync(b, dateA, now, cancellationToken).ConfigureAwait(false);
        if (refusedB is not null)
        {
            return SwapResult.Failed(refusedB);
        }

        if (await SaveBookingsAsync(cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return SwapResult.Failed(refused);
        }

        db.StandingExceptions.AddRange(
            Change(a, dateA, StandingDayChange.MovedTo, dateB, bookingA.Id, userId, now),
            Change(b, dateB, StandingDayChange.MovedTo, dateA, bookingB.Id, userId, now),
            Change(a, dateB, StandingDayChange.MovedFrom, dateA, movedA!.Id, userId, now),
            Change(b, dateA, StandingDayChange.MovedFrom, dateB, movedB!.Id, userId, now));

        var key = $"{StandingTexts.Iso(dateA)}:{StandingTexts.Iso(dateB)}";
        db.OutboxMessages.Add(AvailabilityChange.Message(a.InstitutionId, Min(dateA, dateB), Max(dateA, dateB), now));
        db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.Swapped, a.PublicId, dateB, key, now, other: dateA));
        db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.Swapped, b.PublicId, dateA, key, now, other: dateB));

        return SwapResult.Ok(null, null);
    }

    private async Task<SwapResult> ForGoodAsync(StandingSponsorship a, DateOnly dateA, StandingSponsorship b, DateOnly dateB, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        if (await db.StandingExceptions.AnyAsync(
                e => (e.StandingSponsorshipId == a.Id && e.ExceptionDate >= dateA) || (e.StandingSponsorshipId == b.Id && e.ExceptionDate >= dateB),
                cancellationToken).ConfigureAwait(false))
        {
            return SwapResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }

        // Tracked for the first time here, under the row locks.
        var oldA = await db.StandingSponsorships.SingleAsync(s => s.Id == a.Id, cancellationToken).ConfigureAwait(false);
        var oldB = await db.StandingSponsorships.SingleAsync(s => s.Id == b.Id, cancellationToken).ConfigureAwait(false);
        oldA.EndBefore(dateA, userId, now);
        oldB.EndBefore(dateB, userId, now);

        // A gives on B's days from B's day, and B on A's from A's.
        var newA = oldA.TakingOver(b, dateB, Guid.CreateVersion7(now), userId, now);
        var newB = oldB.TakingOver(a, dateA, Guid.CreateVersion7(now), userId, now);
        db.StandingSponsorships.AddRange(newA, newB);

        var fromA = await ActiveBookingsFromAsync(a.Id, dateA, cancellationToken).ConfigureAwait(false);
        var fromB = await ActiveBookingsFromAsync(b.Id, dateB, cancellationToken).ConfigureAwait(false);
        foreach (var (booking, _) in fromA.Concat(fromB))
        {
            booking.CancelAsInstitution(SwappedReason, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (booking, _) in fromA.Concat(fromB))
        {
            await inventory.ReleaseAsync(booking.SlotInstanceId, booking.Headcount, cancellationToken).ConfigureAwait(false);
        }

        // Each booked day goes to the sponsorship that now gives on it.
        foreach (var (sponsorship, days) in new[] { (newB, fromA), (newA, fromB) })
        {
            foreach (var (_, date) in days)
            {
                var (_, refused) = await BookAsync(sponsorship, date, now, cancellationToken).ConfigureAwait(false);
                if (refused is not null)
                {
                    return SwapResult.Failed(refused);
                }
            }
        }

        if (await SaveBookingsAsync(cancellationToken).ConfigureAwait(false) is { } failed)
        {
            return SwapResult.Failed(failed);
        }

        var moved = fromA.Concat(fromB).Select(d => d.Date).ToList();
        if (moved.Count > 0)
        {
            db.OutboxMessages.Add(AvailabilityChange.Message(a.InstitutionId, moved.Min(), moved.Max(), now));
        }

        var key = $"on:{StandingTexts.Iso(dateA)}:{StandingTexts.Iso(dateB)}";
        db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.SwappedOn, a.PublicId, dateB, key, now, other: dateA));
        db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.SwappedOn, b.PublicId, dateA, key, now, other: dateB));

        return SwapResult.Ok(newA.PublicId, newB.PublicId);
    }

    /// <summary>
    /// <paramref name="sponsorship"/>'s booking on <paramref name="date"/>, through the engine's rules
    /// and the conditional UPDATE, added for the caller to save; or the engine's refusal.
    /// </summary>
    private async Task<(Booking? Booking, string? Refused)> BookAsync(StandingSponsorship sponsorship, DateOnly date, DateTime now, CancellationToken cancellationToken)
    {
        var (slot, refused) = await BookMealHandler
            .CheckAsync(db, sponsorship.MealSlotId, date, now, sponsorship.Headcount, sponsorship.FulfilmentMode, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return (null, refused);
        }

        var instance = await inventory.GetOrCreateAsync(slot, date, cancellationToken).ConfigureAwait(false);
        if (!await inventory.TryTakeAsync(instance.Id, sponsorship.Headcount, cancellationToken).ConfigureAwait(false))
        {
            return (null, BookingErrors.SlotFull);
        }

        var donor = await db.InstitutionDonors
            .AsNoTracking()
            .Where(d => d.Id == sponsorship.InstitutionDonorId)
            .Select(d => new { d.FullName, d.Phone, d.UserId })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var bookingNo = await numbers
            .NextAsync(NumberSeriesDefinition.Booking, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var booking = Booking.Standing(bookingNo, sponsorship, instance.Id, donor.UserId, donor.FullName, donor.Phone, sponsorship.CreatedBy, now);
        db.Bookings.Add(booking);
        return (booking, null);
    }

    /// <summary>Saves the bookings made; <see cref="BookingErrors.AlreadyBooked"/> when a donor holds the day already.</summary>
    private async Task<string?> SaveBookingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == DonorPerSlotIndex)
        {
            return BookingErrors.AlreadyBooked;
        }
    }

    private Task<StandingSponsorship?> FindAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.StandingSponsorships
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.PublicId == publicId && s.InstitutionId == institutionId, cancellationToken);

    private Task<Booking?> ActiveBookingAsync(int sponsorshipId, DateOnly date, CancellationToken cancellationToken) =>
        db.Bookings
            .Where(b => b.StandingSponsorshipId == sponsorshipId && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { Booking = b, i.SlotDate })
            .Where(d => d.SlotDate == date)
            .Select(d => d.Booking)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<List<(Booking Booking, DateOnly Date)>> ActiveBookingsFromAsync(int sponsorshipId, DateOnly from, CancellationToken cancellationToken) =>
        [.. (await db.Bookings
                .Where(b => b.StandingSponsorshipId == sponsorshipId && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
                .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { Booking = b, i.SlotDate })
                .Where(d => d.SlotDate >= from)
                .OrderBy(d => d.SlotDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(d => (d.Booking, d.SlotDate))];

    private static StandingDayChange Change(StandingSponsorship sponsorship, DateOnly day, string kind, DateOnly other, long bookingId, Guid userId, DateTime now) =>
        new()
        {
            InstitutionId = sponsorship.InstitutionId,
            StandingSponsorshipId = sponsorship.Id,
            ExceptionDate = day,
            Kind = kind,
            OtherDate = other,
            BookingId = bookingId,
            CreatedBy = userId,
            CreatedAt = now,
        };

    private static DateOnly Min(DateOnly x, DateOnly y) => x < y ? x : y;

    private static DateOnly Max(DateOnly x, DateOnly y) => x > y ? x : y;
}

/// <summary>The outcome of a swap.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: <c>a</c>, <c>b</c>.</param>
/// <param name="NewA">From now on: the sponsorship that now gives A's meal on B's days.</param>
/// <param name="NewB">From now on: B's meal on A's days.</param>
public sealed record SwapResult(string? Error, IReadOnlyList<string> Fields, Guid? NewA, Guid? NewB)
{
    public const string InvalidDetails = SponsorshipChangeResult.InvalidDetails;

    public bool Succeeded => Error is null;

    internal static SwapResult Ok(Guid? newA, Guid? newB) => new(null, [], newA, newB);

    internal static SwapResult Failed(string error) => new(error, [], null, null);

    internal static SwapResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null);
}

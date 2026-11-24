using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>
/// A donor asks for a day a regular donor holds; the institution gives it or declines (plan 08, K6).
/// </summary>
/// <remarks>
/// <para>
/// Asking holds nothing. It is accepted only for a day the public calendar shows as held
/// (<see cref="MealSlotRules.IsHeld"/>): a day with room is booked, not asked for.
/// </para>
/// <para>
/// Giving is one transaction: the slot-day's Redis lock, then the sponsorship's row lock
/// (<see cref="IStandingLocks"/>, K19), and under it the standing booking cancelled and its place given
/// back, the asker booked through the engine's rules and conditional UPDATE and approved, the day
/// recorded as <see cref="StandingDayChange.Released"/> so the job never books it again, the request
/// marked given, and the texts and the calendars' hint written. Anything refused on the way (the asker's
/// headcount does not fit, the donor holds the day already) leaves all of it as it was.
/// </para>
/// </remarks>
public sealed class DayRequestHandler(
    ISethikaDbContext db,
    ISlotLocks locks,
    IStandingLocks standingLocks,
    ISlotInventory inventory,
    INumberSeries numbers,
    IStandingHolds holds,
    TimeProvider timeProvider)
{
    /// <summary>The standing booking's cancel reason when its day is given to someone who asked.</summary>
    public const string GivenReason = "Given to a donor who asked for this day.";

    private const string OpenPerDonorIndex = "UX_DayRequests_OpenPerDonor";
    private const string DonorPerSlotIndex = "UX_Bookings_DonorPerSlot";
    private const string ExceptionDayIndex = "UQ_StandingExc_Day";

    /// <summary>The signed-in donor asks <paramref name="slug"/> for a held day.</summary>
    public async Task<DayRequestResult> AskAsync(Guid donorId, string slug, DayRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invalid = new List<string>();
        if (input.MealSlot is null)
        {
            invalid.Add("mealSlot");
        }

        if (input.Date is not { } date || date < SriLankaTime.Today(now))
        {
            invalid.Add("date");
        }

        if (input.Headcount is not > 0)
        {
            invalid.Add("headcount");
        }

        var fulfilment = Tidy(input.Fulfilment);
        if (fulfilment is null)
        {
            invalid.Add("fulfilment");
        }

        var note = Tidy(input.Note);
        if (note is { Length: > DayRequest.MaxText })
        {
            invalid.Add("note");
        }

        if (invalid.Count > 0)
        {
            return DayRequestResult.Invalid(invalid);
        }

        var day = input.Date!.Value;
        var slot = await db.MealSlots
            .AsNoTracking()
            .Where(s => s.PublicId == input.MealSlot && s.IsActive)
            .Join(db.Institutions.Where(i => i.Slug == slug), s => s.InstitutionId, i => i.Id, (s, i) => s)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (slot is null)
        {
            return DayRequestResult.Failed(DayRequestResult.NotFound);
        }

        if (await DonorAsync(donorId, cancellationToken).ConfigureAwait(false) is not { } donor)
        {
            return DayRequestResult.Failed(DayRequestResult.PhoneRequired);
        }

        // The asker's own booking would be refused the same way when the day is given.
        var (_, refused) = await BookMealHandler
            .CheckAsync(db, slot.Id, day, now, input.Headcount!.Value, fulfilment!, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return DayRequestResult.Failed(refused);
        }

        if (!await HeldAsync(slot, day, now, cancellationToken).ConfigureAwait(false))
        {
            return DayRequestResult.Failed(DayRequestResult.NotHeld);
        }

        var holding = await db.Bookings
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { b.DonorPhone, b.Status, i.MealSlotId, i.SlotDate })
            .AnyAsync(
                b => b.MealSlotId == slot.Id && b.SlotDate == day && b.DonorPhone == donor.Phone
                     && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved),
                cancellationToken)
            .ConfigureAwait(false);

        if (holding)
        {
            return DayRequestResult.Failed(BookingErrors.AlreadyBooked);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var request = new DayRequest
        {
            RequestNo = await numbers
                .NextAsync(NumberSeriesDefinition.DayRequest, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
                .ConfigureAwait(false),
            InstitutionId = slot.InstitutionId,
            MealSlotId = slot.Id,
            RequestedDate = day,
            DonorUserId = donorId,
            Headcount = input.Headcount.Value,
            FulfilmentMode = fulfilment!,
            Note = note,
            CreatedAt = now,
        };

        db.DayRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == OpenPerDonorIndex)
        {
            return DayRequestResult.Failed(DayRequestResult.AlreadyAsked);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DayRequestResult.Ok(request.RequestNo);
    }

    /// <summary>
    /// Gives the day of request <paramref name="requestNo"/> to the donor who asked: one transaction
    /// lets the standing day go and books the asker, approved. <paramref name="sponsorship"/> names whose
    /// day it is when more than one regular donor holds it.
    /// </summary>
    public async Task<DayRequestResult> GiveAsync(int institutionId, Guid userId, string requestNo, Guid? sponsorship, CancellationToken cancellationToken = default)
    {
        var asked = await FindAsync(institutionId, requestNo, cancellationToken).ConfigureAwait(false);
        if (asked is null)
        {
            return DayRequestResult.Failed(DayRequestResult.NotFound);
        }

        if (!asked.IsOpen)
        {
            return DayRequestResult.Failed(DayRequestResult.Answered);
        }

        await using var slotLock = await locks.AcquireAsync(asked.MealSlotId, asked.RequestedDate, cancellationToken).ConfigureAwait(false);
        if (slotLock.Outcome == LockOutcome.Busy)
        {
            return DayRequestResult.Failed(BookMealResult.SlotBusy);
        }

        // The standing bookings on the day, read only to know whose row to lock: read again under it.
        var standing = await db.Bookings
            .AsNoTracking()
            .Where(b => b.InstitutionId == institutionId && b.Source == Booking.StandingSource
                        && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved))
            .Join(db.SlotInstances, b => b.SlotInstanceId, i => i.Id, (b, i) => new { b.Id, b.StandingSponsorshipId, i.MealSlotId, i.SlotDate })
            .Where(b => b.MealSlotId == asked.MealSlotId && b.SlotDate == asked.RequestedDate)
            .Join(db.StandingSponsorships, b => b.StandingSponsorshipId, s => s.Id, (b, s) => new { b.Id, SponsorshipId = s.Id, s.PublicId })
            .Where(b => sponsorship == null || b.PublicId == sponsorship)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (standing.Count == 0)
        {
            return DayRequestResult.Failed(DayRequestResult.NotHeld);
        }

        if (standing.Count > 1)
        {
            return DayRequestResult.Invalid(["sponsorship"]);
        }

        var held = standing[0];
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (!await standingLocks.LockActiveAsync(held.SponsorshipId, cancellationToken).ConfigureAwait(false))
        {
            return DayRequestResult.Failed(SponsorshipChangeResult.Ended);
        }

        // Read under the lock, and tracked for the first time: what a give that got there first left.
        var request = await db.DayRequests.SingleAsync(r => r.Id == asked.Id, cancellationToken).ConfigureAwait(false);
        var released = await db.Bookings.SingleAsync(b => b.Id == held.Id, cancellationToken).ConfigureAwait(false);
        if (!request.IsOpen)
        {
            return DayRequestResult.Failed(DayRequestResult.Answered);
        }

        if (released.Status is not (BookingStatus.Requested or BookingStatus.Approved))
        {
            return DayRequestResult.Failed(DayRequestResult.NotHeld);
        }

        if (await db.StandingExceptions.AnyAsync(e => e.StandingSponsorshipId == held.SponsorshipId && e.ExceptionDate == request.RequestedDate, cancellationToken).ConfigureAwait(false))
        {
            // A swap brought this sponsor to the day (MovedFrom): the day has its one change already.
            return DayRequestResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }

        if (await DonorAsync(request.DonorUserId, cancellationToken).ConfigureAwait(false) is not { } donor)
        {
            return DayRequestResult.Failed(DayRequestResult.PhoneRequired);
        }

        released.CancelAsInstitution(GivenReason, userId, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DayRequestResult.Failed(DayRequestResult.Stale);
        }

        await inventory.ReleaseAsync(released.SlotInstanceId, released.Headcount, cancellationToken).ConfigureAwait(false);

        // The asker's booking, as the engine makes any: its rules, then the conditional UPDATE.
        var (slot, refused) = await BookMealHandler
            .CheckAsync(db, request.MealSlotId, request.RequestedDate, now, request.Headcount, request.FulfilmentMode, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return DayRequestResult.Failed(refused);
        }

        var instance = await inventory.GetOrCreateAsync(slot, request.RequestedDate, cancellationToken).ConfigureAwait(false);
        if (!await inventory.TryTakeAsync(instance.Id, request.Headcount, cancellationToken).ConfigureAwait(false))
        {
            // Disposing the transaction puts the standing day back as it was.
            return DayRequestResult.Failed(BookingErrors.SlotFull);
        }

        var bookingNo = await numbers
            .NextAsync(NumberSeriesDefinition.Booking, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var booking = Booking.Request(bookingNo, slot, instance.Id, request.DonorUserId, donor.Name, donor.Phone, request.Headcount, request.FulfilmentMode, now);
        booking.Notes = request.Note;
        if (booking.Status == BookingStatus.Requested)
        {
            booking.Approve(userId, now);
        }

        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == DonorPerSlotIndex)
        {
            return DayRequestResult.Failed(BookingErrors.AlreadyBooked);
        }

        var publicId = await db.StandingSponsorships
            .Where(s => s.Id == held.SponsorshipId)
            .Select(s => s.PublicId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        db.StandingExceptions.Add(new StandingDayChange
        {
            InstitutionId = institutionId,
            StandingSponsorshipId = held.SponsorshipId,
            ExceptionDate = request.RequestedDate,
            Kind = StandingDayChange.Released,
            BookingId = released.Id,
            DayRequestId = request.Id,
            CreatedBy = userId,
            CreatedAt = now,
        });

        request.Give(held.SponsorshipId, booking.Id, userId, now);
        db.OutboxMessages.Add(AvailabilityChange.Message(institutionId, request.RequestedDate, request.RequestedDate, now));
        db.OutboxMessages.Add(StandingTexts.Message(StandingTexts.Released, publicId, request.RequestedDate, StandingTexts.Iso(request.RequestedDate), now));
        db.OutboxMessages.Add(StandingTexts.Answered(request.RequestNo, given: true, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == ExceptionDayIndex)
        {
            return DayRequestResult.Failed(SponsorshipChangeResult.DayAlreadyChanged);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DayRequestResult.Failed(DayRequestResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DayRequestResult.Ok(request.RequestNo, bookingNo);
    }

    /// <summary>Declines request <paramref name="requestNo"/>; the day stays the regular donor's.</summary>
    public async Task<DayRequestResult> DeclineAsync(int institutionId, Guid userId, string requestNo, string? reason, CancellationToken cancellationToken = default)
    {
        if (Tidy(reason) is { Length: > DayRequest.MaxText })
        {
            return DayRequestResult.Invalid(["reason"]);
        }

        var request = await db.DayRequests
            .SingleOrDefaultAsync(r => r.RequestNo == requestNo && r.InstitutionId == institutionId, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return DayRequestResult.Failed(DayRequestResult.NotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!request.Decline(reason, userId, now))
        {
            return DayRequestResult.Failed(DayRequestResult.Answered);
        }

        db.OutboxMessages.Add(StandingTexts.Answered(request.RequestNo, given: false, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DayRequestResult.Failed(DayRequestResult.Stale);
        }

        return DayRequestResult.Ok(request.RequestNo);
    }

    /// <summary>Whether the public calendar shows <paramref name="day"/> of <paramref name="slot"/> as held now.</summary>
    private async Task<bool> HeldAsync(MealSlot slot, DateOnly day, DateTime now, CancellationToken cancellationToken)
    {
        var hold = (await holds.ListAsync(slot.InstitutionId, day, day, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(h => h.MealSlotId == slot.Id);

        if (hold is null)
        {
            return false;
        }

        var instance = await db.SlotInstances
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.MealSlotId == slot.Id && i.SlotDate == day, cancellationToken)
            .ConfigureAwait(false);

        // The day's rules were asked already (CheckAsync); this asks only whether it is full, and why.
        var openings = MealSlotRules.Openings(slot, MealSlotRules.ActiveInstitution, [], [], day, now, instance);
        return MealSlotRules.IsHeld(slot, openings, instance, hold.Sponsors, hold.Headcount);
    }

    private Task<DayRequest?> FindAsync(int institutionId, string requestNo, CancellationToken cancellationToken) =>
        db.DayRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.RequestNo == requestNo && r.InstitutionId == institutionId, cancellationToken);

    private async Task<(string Name, string Phone)?> DonorAsync(Guid donorId, CancellationToken cancellationToken)
    {
        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == donorId)
            .Select(u => new { u.FullName, u.PhoneNumber, u.PhoneConfirmed })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return donor is { PhoneConfirmed: true, PhoneNumber: { Length: > 0 } phone } ? (donor.FullName, phone) : null;
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

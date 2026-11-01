using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Engine;

/// <summary>
/// The booking engine (plan 03, 5.3): a signed-in donor takes a place on a slot-day, and the yearly
/// job takes one for a donor's yearly booking (plan 07, H3).
/// </summary>
/// <remarks>
/// In this order, and the order is the design:
/// <list type="number">
/// <item>The Redis lock on the slot-day (C2, layer 1), so attempts on it run one at a time and a
/// loser is answered quickly. If Redis is down the booking goes on without it.</item>
/// <item>One transaction that reads the slot, its seasons and blocks fresh and asks
/// <see cref="MealSlotRules.CanBook"/>; gets or creates the slot-day (C3); takes the place with the
/// conditional UPDATE (C2, layers 2 and 3); numbers the booking; and saves it with its history and
/// its outbox message.</item>
/// <item>Commit, then release the lock.</item>
/// </list>
/// Anything that fails after the place was taken rolls it back with the transaction: a donor who
/// already holds the slot-day (<c>UX_Bookings_DonorPerSlot</c>) gives the place they just took back
/// without the handler doing anything. A yearly booking goes through the same steps, so it can never
/// take a place the engine would refuse a donor.
/// </remarks>
public sealed class BookMealHandler(
    ISethikaDbContext db,
    ISlotLocks locks,
    ISlotInventory inventory,
    INumberSeries numbers,
    TimeProvider timeProvider)
{
    /// <summary>Written when a booking waits for the institution's decision.</summary>
    public const string RequestedEvent = "BookingRequested";

    /// <summary>Written when a slot needs no approval, so the booking is confirmed at once.</summary>
    public const string ConfirmedEvent = "BookingConfirmed";

    /// <summary>Written when the yearly job could not make a year's booking (H4); the donor is texted why.</summary>
    public const string YearNotBookedEvent = "RecurringBookingNotMade";

    /// <summary><see cref="YearNotBookedEvent"/>'s reason when every place on the day was taken.</summary>
    public const string FullReason = "full";

    /// <summary><see cref="YearNotBookedEvent"/>'s reason for everything else: the meal is not open that day.</summary>
    public const string ClosedReason = "closed";

    private const string DonorPerSlotIndex = "UX_Bookings_DonorPerSlot";
    private const string YearlyPerSlotDayIndex = "UX_Recurring_DonorSlotDay";
    private const int MaxInMemoryOf = 200;
    private const int MaxNotes = 500;

    public async Task<BookMealResult> BookAsync(Guid donorId, BookMealCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(command.Fulfilment))
        {
            invalid.Add("fulfilment");
        }

        if (command.InMemoryOf is { Length: > MaxInMemoryOf })
        {
            invalid.Add("inMemoryOf");
        }

        if (command.Notes is { Length: > MaxNotes })
        {
            invalid.Add("notes");
        }

        // A booking for an observance names the plan and the observance, or neither.
        if ((command.MemorialPlan is null) != string.IsNullOrWhiteSpace(command.MemorialRule))
        {
            invalid.Add(command.MemorialPlan is null ? "memorialPlan" : "memorialRule");
        }

        if (invalid.Count > 0)
        {
            return BookMealResult.Invalid(invalid);
        }

        if (await DonorAsync(donorId, cancellationToken).ConfigureAwait(false) is not { } donor)
        {
            return BookMealResult.Failed(BookMealResult.PhoneRequired);
        }

        Observance? observance = null;
        if (command.MemorialPlan is { } plan)
        {
            observance = await ObservanceAsync(donorId, plan, command.MemorialRule!.Trim(), cancellationToken).ConfigureAwait(false);
            if (observance is null)
            {
                return BookMealResult.Failed(BookMealResult.MemorialNotFound);
            }

            // The day is the observance's own, so the planner can tell it is booked.
            if (MemorialObservances.NumberOn(observance.DateOfPassing, observance.Rule, command.Date) is null)
            {
                return BookMealResult.Failed(BookMealResult.NotAnObservance);
            }

            // Every year belongs to an observance kept every year; a 7th day comes once.
            if (command.EveryYear && !observance.Rule.RepeatsAnnually)
            {
                return BookMealResult.Invalid(["everyYear"]);
            }
        }

        // The lock is keyed by the slot's internal id, which never leaves the server.
        var slotId = await db.MealSlots
            .Where(s => s.PublicId == command.MealSlot)
            .Select(s => (int?)s.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (slotId is not { } id)
        {
            return BookMealResult.Failed(BookMealResult.SlotNotFound);
        }

        await using var slotLock = await locks.AcquireAsync(id, command.Date, cancellationToken).ConfigureAwait(false);
        if (slotLock.Outcome == LockOutcome.Busy)
        {
            return BookMealResult.Failed(BookMealResult.SlotBusy);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var fulfilment = command.Fulfilment!.Trim();
        var (slot, refused) = await CheckAsync(id, command.Date, now, command.Headcount, fulfilment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return BookMealResult.Failed(refused);
        }

        var instance = await inventory.GetOrCreateAsync(slot, command.Date, cancellationToken).ConfigureAwait(false);

        if (!await inventory.TryTakeAsync(instance.Id, command.Headcount, cancellationToken).ConfigureAwait(false))
        {
            return BookMealResult.Failed(BookingErrors.SlotFull);
        }

        var booking = await AddBookingAsync(slot, instance.Id, command.Date, donorId, donor, command.Headcount, fulfilment, now, cancellationToken).ConfigureAwait(false);
        booking.InMemoryOf = Tidy(command.InMemoryOf);
        booking.Notes = Tidy(command.Notes);

        if (observance is not null)
        {
            booking.Source = Booking.MemorialSource;
            booking.MemorialPlanId = observance.PlanId;
            booking.MemorialRuleId = observance.Rule.Id;
        }

        // After the plan is set: a yearly booking started here keeps the plan (H6).
        if (command.EveryYear)
        {
            await StartYearlyAsync(booking, slot.Id, command.Date, now, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName is DonorPerSlotIndex or YearlyPerSlotDayIndex)
        {
            // Disposing the transaction rolls back the place taken above. The yearly index only
            // collides when the same donor books the same slot-day twice at once: the other attempt
            // holds it.
            return BookMealResult.Failed(BookingErrors.AlreadyBooked);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return BookMealResult.Ok(booking.BookingNo, booking.Status);
    }

    /// <summary>
    /// One year of the yearly booking <paramref name="recurringId"/>, for the yearly job (H3, H4): book
    /// it through the engine, or text the donor why not. Either way the year is claimed, so it is tried
    /// once; two runs at once meet on the claim and the second does nothing.
    /// </summary>
    /// <remarks>
    /// The claim is a conditional UPDATE of <c>LastGeneratedYear</c> in the booking's own transaction:
    /// it commits with the booking or the refusal's outbox message, or not at all. A busy lock or a
    /// collision with the donor's own booking made a moment ago claims nothing, and the next run tries
    /// again. Runs as the platform, so the donor's and the institution's rows are both visible.
    /// </remarks>
    public async Task<RecurringYearResult> BookYearAsync(int recurringId, int year, CancellationToken cancellationToken = default)
    {
        try
        {
            return await BookYearCoreAsync(recurringId, year, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // One context serves a whole run: nothing from this year may reach the next one's save.
            db.ChangeTracker.Clear();
        }
    }

    private async Task<RecurringYearResult> BookYearCoreAsync(int recurringId, int year, CancellationToken cancellationToken)
    {
        var yearly = await db.RecurringBookings
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == recurringId, cancellationToken)
            .ConfigureAwait(false);

        if (yearly is not { IsActive: true })
        {
            return RecurringYearResult.Of(RecurringYearOutcome.AlreadyTried);
        }

        var date = yearly.OccursIn(year);
        var donor = await DonorAsync(yearly.DonorUserId, cancellationToken).ConfigureAwait(false);

        await using var slotLock = await locks.AcquireAsync(yearly.MealSlotId, date, cancellationToken).ConfigureAwait(false);
        if (slotLock.Outcome == LockOutcome.Busy)
        {
            return RecurringYearResult.Of(RecurringYearOutcome.Busy);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var claimed = await db.RecurringBookings
            .Where(r => r.Id == recurringId && r.IsActive && (r.LastGeneratedYear == null || r.LastGeneratedYear < year))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastGeneratedYear, (short)year), cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return RecurringYearResult.Of(RecurringYearOutcome.AlreadyTried);
        }

        RecurringYearResult result;
        if (date < SriLankaTime.Today(now))
        {
            // The job did not run while this year was open; there is nothing left to book or to tell.
            result = RecurringYearResult.Of(RecurringYearOutcome.Past);
        }
        else if (donor is null)
        {
            // Bookings and texts both need a confirmed phone; the donor sees the year missing.
            result = RecurringYearResult.Of(RecurringYearOutcome.NoPhone);
        }
        else
        {
            result = await TakeYearAsync(yearly, year, date, donor, now, cancellationToken).ConfigureAwait(false);
        }

        if (result.Outcome == RecurringYearOutcome.Busy)
        {
            // Disposing the transaction gives back the claim and anything taken with it.
            return result;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<RecurringYearResult> TakeYearAsync(RecurringBooking yearly, int year, DateOnly date, Donor donor, DateTime now, CancellationToken cancellationToken)
    {
        var (slot, refused) = await CheckAsync(yearly.MealSlotId, date, now, yearly.Headcount, yearly.FulfilmentMode, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            NotBooked(yearly, year, date, ClosedReason, now);
            return await SaveAsync(new RecurringYearResult(RecurringYearOutcome.NotBooked, refused, null), cancellationToken).ConfigureAwait(false);
        }

        var instance = await inventory.GetOrCreateAsync(slot, date, cancellationToken).ConfigureAwait(false);

        // A donor who booked the day themselves already has it: no second booking, and nothing to say.
        var held = await db.Bookings
            .AnyAsync(
                b => b.SlotInstanceId == instance.Id && b.DonorPhone == donor.Phone
                     && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Approved),
                cancellationToken)
            .ConfigureAwait(false);

        if (held)
        {
            return RecurringYearResult.Of(RecurringYearOutcome.AlreadyHeld);
        }

        if (!await inventory.TryTakeAsync(instance.Id, yearly.Headcount, cancellationToken).ConfigureAwait(false))
        {
            NotBooked(yearly, year, date, FullReason, now);
            return await SaveAsync(new RecurringYearResult(RecurringYearOutcome.NotBooked, BookingErrors.SlotFull, null), cancellationToken).ConfigureAwait(false);
        }

        var booking = await AddBookingAsync(slot, instance.Id, date, yearly.DonorUserId, donor, yearly.Headcount, yearly.FulfilmentMode, now, cancellationToken).ConfigureAwait(false);
        booking.Source = Booking.RecurringSource;
        booking.RecurringBookingId = yearly.Id;
        booking.MemorialPlanId = yearly.MemorialPlanId;
        booking.OccasionLookupId = yearly.OccasionLookupId;
        booking.InMemoryOf = yearly.InMemoryOf;

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == DonorPerSlotIndex)
        {
            // The donor booked the same day between the check above and this save: try again tomorrow,
            // when the check will find it.
            return RecurringYearResult.Of(RecurringYearOutcome.Busy);
        }

        return new RecurringYearResult(RecurringYearOutcome.Booked, null, booking.BookingNo);
    }

    private async Task<RecurringYearResult> SaveAsync(RecurringYearResult result, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private void NotBooked(RecurringBooking yearly, int year, DateOnly date, string reason, DateTime now) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = YearNotBookedEvent,
            AggregateType = nameof(RecurringBooking),
            AggregateId = yearly.PublicId.ToString(),
            Payload = JsonSerializer.Serialize(new
            {
                recurring = yearly.PublicId,
                year,
                date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reason,
            }),
            OccurredAt = now,
        });

    /// <summary>
    /// Reads the slot, its institution's status and its blocks fresh, inside the lock, and asks the
    /// rules (C4): a block added or a slot switched off a moment ago counts.
    /// </summary>
    private async Task<(MealSlot Slot, string? Refused)> CheckAsync(int slotId, DateOnly date, DateTime now, short headcount, string fulfilment, CancellationToken cancellationToken)
    {
        var slot = await db.MealSlots
            .AsNoTracking()
            .Include(s => s.Seasons)
            .SingleAsync(s => s.Id == slotId, cancellationToken)
            .ConfigureAwait(false);

        var institutionStatus = await db.Institutions
            .Where(i => i.Id == slot.InstitutionId)
            .Select(i => i.Status)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var blocks = await db.BlockedDates
            .AsNoTracking()
            .Where(b => b.InstitutionId == slot.InstitutionId && b.DateFrom <= date && b.DateTo >= date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (slot, MealSlotRules.CanBook(slot, institutionStatus, slot.Seasons, blocks, date, now, headcount, fulfilment));
    }

    /// <summary>
    /// Numbers the booking (C7) and adds it with its outbox message, and the hint to the institution's
    /// open calendars that <paramref name="date"/> changed (plan 07, H10); the caller saves.
    /// </summary>
    private async Task<Booking> AddBookingAsync(MealSlot slot, long instanceId, DateOnly date, Guid donorId, Donor donor, short headcount, string fulfilment, DateTime now, CancellationToken cancellationToken)
    {
        var bookingNo = await numbers
            .NextAsync(NumberSeriesDefinition.Booking, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var booking = Booking.Request(bookingNo, slot, instanceId, donorId, donor.Name, donor.Phone, headcount, fulfilment, now);
        db.Bookings.Add(booking);

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = booking.Status == BookingStatus.Approved ? ConfirmedEvent : RequestedEvent,
            AggregateType = nameof(Booking),
            AggregateId = bookingNo,
            Payload = JsonSerializer.Serialize(new { bookingNo, donorUserId = donorId }),
            OccurredAt = now,
        });

        db.OutboxMessages.Add(AvailabilityChange.Message(slot.InstitutionId, date, date, now));

        return booking;
    }

    /// <summary>
    /// "Book this every year" (H5): the booking joins the donor's yearly booking of this meal on this
    /// day, or starts one from next year.
    /// </summary>
    private async Task StartYearlyAsync(Booking booking, int slotId, DateOnly date, DateTime now, CancellationToken cancellationToken)
    {
        var existing = await db.RecurringBookings
            .Where(r => r.DonorUserId == booking.DonorUserId && r.MealSlotId == slotId
                        && r.RecurMonth == date.Month && r.RecurDay == date.Day && r.IsActive)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } id)
        {
            booking.RecurringBookingId = id;
            return;
        }

        booking.RecurringBooking = RecurringBooking.From(booking, slotId, date, Guid.CreateVersion7(now), now);
    }

    /// <summary>The donor's name and confirmed phone, or <see langword="null"/> without one: a booking's texts go there.</summary>
    private async Task<Donor?> DonorAsync(Guid donorId, CancellationToken cancellationToken)
    {
        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == donorId)
            .Select(u => new { u.FullName, u.PhoneNumber, u.PhoneConfirmed })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return donor is { PhoneConfirmed: true, PhoneNumber: { Length: > 0 } phone } ? new Donor(donor.FullName, phone) : null;
    }

    /// <summary>
    /// The donor's active plan <paramref name="planId"/> and its faith's active observance
    /// <paramref name="rule"/>, or <see langword="null"/>. The donor id is checked as well as
    /// Row-Level Security, so a staff session could not book for someone else's plan.
    /// </summary>
    private async Task<Observance?> ObservanceAsync(Guid donorId, Guid planId, string rule, CancellationToken cancellationToken)
    {
        var plan = await db.MemorialPlans
            .AsNoTracking()
            .Where(p => p.PublicId == planId && p.DonorUserId == donorId && p.IsActive)
            .Select(p => new { p.Id, p.DateOfPassing, p.ReligionLookupId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (plan is null)
        {
            return null;
        }

        var found = await db.MemorialRules
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Code == rule && r.IsActive && r.ReligionLookupId == plan.ReligionLookupId, cancellationToken)
            .ConfigureAwait(false);

        return found is null ? null : new Observance(plan.Id, plan.DateOfPassing, found);
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private sealed record Donor(string Name, string Phone);

    private sealed record Observance(int PlanId, DateOnly DateOfPassing, MemorialRule Rule);
}

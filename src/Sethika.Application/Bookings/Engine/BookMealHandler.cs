using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Engine;

/// <summary>
/// The booking engine (plan 03, 5.3): a signed-in donor takes a place on a slot-day.
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
/// without the handler doing anything.
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

    private const string DonorPerSlotIndex = "UX_Bookings_DonorPerSlot";
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

        if (invalid.Count > 0)
        {
            return BookMealResult.Invalid(invalid);
        }

        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == donorId)
            .Select(u => new { u.FullName, u.PhoneNumber, u.PhoneConfirmed })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (donor is not { PhoneConfirmed: true, PhoneNumber: { Length: > 0 } phone })
        {
            return BookMealResult.Failed(BookMealResult.PhoneRequired);
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
        if (slotLock.Outcome == SlotLockOutcome.Busy)
        {
            return BookMealResult.Failed(BookMealResult.SlotBusy);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Read fresh, inside the lock: a block added or a slot switched off a moment ago counts.
        var slot = await db.MealSlots
            .AsNoTracking()
            .Include(s => s.Seasons)
            .SingleAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        var institutionStatus = await db.Institutions
            .Where(i => i.Id == slot.InstitutionId)
            .Select(i => i.Status)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var blocks = await db.BlockedDates
            .AsNoTracking()
            .Where(b => b.InstitutionId == slot.InstitutionId && b.DateFrom <= command.Date && b.DateTo >= command.Date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fulfilment = command.Fulfilment!.Trim();
        var refused = MealSlotRules.CanBook(slot, institutionStatus, slot.Seasons, blocks, command.Date, now, command.Headcount, fulfilment);
        if (refused is not null)
        {
            return BookMealResult.Failed(refused);
        }

        var instance = await inventory.GetOrCreateAsync(slot, command.Date, cancellationToken).ConfigureAwait(false);

        if (!await inventory.TryTakeAsync(instance.Id, command.Headcount, cancellationToken).ConfigureAwait(false))
        {
            return BookMealResult.Failed(BookingErrors.SlotFull);
        }

        var bookingNo = await numbers
            .NextAsync(NumberSeriesDefinition.Booking, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var booking = Booking.Request(bookingNo, slot, instance.Id, donorId, donor.FullName, phone, command.Headcount, fulfilment, now);
        booking.InMemoryOf = Tidy(command.InMemoryOf);
        booking.Notes = Tidy(command.Notes);
        db.Bookings.Add(booking);

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = booking.Status == BookingStatus.Approved ? ConfirmedEvent : RequestedEvent,
            AggregateType = nameof(Booking),
            AggregateId = bookingNo,
            Payload = JsonSerializer.Serialize(new { bookingNo, donorUserId = donorId }),
            OccurredAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == DonorPerSlotIndex)
        {
            // Disposing the transaction rolls back the place taken above.
            return BookMealResult.Failed(BookingErrors.AlreadyBooked);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return BookMealResult.Ok(bookingNo, booking.Status);
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

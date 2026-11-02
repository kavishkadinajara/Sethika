using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Engine;

/// <summary>What the institution does to a booking on its calendar (plan 03, C5).</summary>
public enum InstitutionDecision
{
    Approve,
    Reject,
    Cancel,
    Complete,
    NoShow,
}

/// <summary>The outcome of a decision: the booking's new status, or why not.</summary>
/// <param name="Error">An API error code (<c>BookingErrors</c> or one of the constants here); <see langword="null"/> on success.</param>
public sealed record BookingDecisionResult(string? Error, string? Status)
{
    /// <summary>No booking with that number that the caller may act on.</summary>
    public const string NotFound = "booking_not_found";

    /// <summary>Someone else changed the booking first; read it again.</summary>
    public const string Stale = "stale";

    public bool Succeeded => Error is null;

    internal static BookingDecisionResult Ok(string status) => new(null, status);

    internal static BookingDecisionResult Failed(string error) => new(error, null);
}

/// <summary>
/// Moves a booking through its life after it was made: the institution's decisions and the donor's
/// own cancellation. A move that gives the place back releases it in the same transaction (C1).
/// </summary>
/// <remarks>
/// Row-Level Security shows a caller only the bookings of their institution and their own. That is
/// not enough here on its own: a staff member who has booked a meal somewhere else as a donor sees
/// that booking too, through the owner half of the predicate. So an institution's decision also
/// checks the booking belongs to the institution the caller is acting for, and a donor's checks it
/// is theirs (the aggregate). Anything else reads as not found.
///
/// Two decisions on one booking at once: the booking's save comes first, so the second waits on the
/// first's row lock, fails on <c>RowVersion</c> once it commits, and never reaches the release.
/// </remarks>
public sealed class BookingDecisionHandler(ISethikaDbContext db, ISlotInventory inventory, TimeProvider timeProvider)
{
    public const string ApprovedEvent = "BookingApproved";
    public const string RejectedEvent = "BookingRejected";
    public const string CancelledEvent = "BookingCancelled";

    /// <summary>A decision by staff of <paramref name="institutionId"/>, the institution they act for.</summary>
    public Task<BookingDecisionResult> DecideAsync(
        int institutionId,
        string bookingNo,
        InstitutionDecision decision,
        string? reason,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            bookingNo,
            booking => booking.InstitutionId == institutionId,
            (booking, slotDate, today, now) => decision switch
            {
                InstitutionDecision.Approve => booking.Approve(userId, now),
                InstitutionDecision.Reject => booking.Reject(reason ?? string.Empty, userId, now),
                InstitutionDecision.Cancel => booking.CancelAsInstitution(reason ?? string.Empty, userId, now),
                InstitutionDecision.Complete => booking.Complete(userId, slotDate, today, now),
                InstitutionDecision.NoShow => booking.MarkNoShow(userId, slotDate, today, now),
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            },
            byDonor: false,
            cancellationToken);

    /// <summary>The donor cancels their own booking, until the day before the meal (Q1).</summary>
    public Task<BookingDecisionResult> CancelAsDonorAsync(
        string bookingNo,
        string? reason,
        Guid donorId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            bookingNo,
            booking => booking.DonorUserId == donorId,
            (booking, slotDate, today, now) => booking.CancelAsDonor(reason, donorId, slotDate, today, now),
            byDonor: true,
            cancellationToken);

    private async Task<BookingDecisionResult> ChangeAsync(
        string bookingNo,
        Func<Booking, bool> mayAct,
        Func<Booking, DateOnly, DateOnly, DateTime, BookingResult> transition,
        bool byDonor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bookingNo))
        {
            return BookingDecisionResult.Failed(BookingDecisionResult.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var booking = await db.Bookings
            .SingleOrDefaultAsync(b => b.BookingNo == bookingNo.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (booking is null || !mayAct(booking))
        {
            return BookingDecisionResult.Failed(BookingDecisionResult.NotFound);
        }

        var slotDate = await db.SlotInstances
            .Where(i => i.Id == booking.SlotInstanceId)
            .Select(i => i.SlotDate)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var heldPlace = BookingStatus.HoldsPlace(booking.Status);

        var result = transition(booking, slotDate, SriLankaTime.Today(now), now);
        if (!result.Succeeded)
        {
            return BookingDecisionResult.Failed(result.Error!);
        }

        var eventType = booking.Status switch
        {
            BookingStatus.Approved => ApprovedEvent,
            BookingStatus.Rejected => RejectedEvent,
            BookingStatus.Cancelled => CancelledEvent,
            _ => null,
        };

        if (eventType is not null)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = eventType,
                AggregateType = nameof(Booking),
                AggregateId = booking.BookingNo,
                Payload = JsonSerializer.Serialize(new { bookingNo = booking.BookingNo, donorUserId = booking.DonorUserId, byDonor }),
                OccurredAt = now,
            });
        }

        // A place given back opens the day again for everyone watching it (plan 07, H10).
        if (heldPlace && !BookingStatus.HoldsPlace(booking.Status))
        {
            db.OutboxMessages.Add(AvailabilityChange.Message(booking.InstitutionId, slotDate, slotDate, now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BookingDecisionResult.Failed(BookingDecisionResult.Stale);
        }

        // After the save, not before: the save is what tells a second decision on the same booking
        // that it lost, and a release ahead of it would take the place back twice.
        if (heldPlace && !BookingStatus.HoldsPlace(booking.Status))
        {
            await inventory.ReleaseAsync(booking.SlotInstanceId, booking.Headcount, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return BookingDecisionResult.Ok(booking.Status);
    }
}

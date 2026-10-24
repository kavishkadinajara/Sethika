using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;

namespace Sethika.Application.Bookings.Notifications;

/// <summary>
/// Texts the donor when their booking is received or confirmed, approved, rejected, or cancelled by
/// the institution (plan 03, C11), in their language.
/// </summary>
/// <remarks>
/// A cancellation by the donor themselves is not texted back to them. The institution gets no texts
/// (Q4). A reason is never sent: the institution typed it, and a text from the Sethika sender id
/// carries no typed text (E2); "sign in to see why" instead.
///
/// The dedup key is the template and the booking: each move happens to a booking at most once, so a
/// re-run of a message - or a second message for the same move - texts nobody twice.
/// </remarks>
public sealed class BookingSmsHandler(IBookingNotificationQueries queries, IUserTextMessages texts) : IOutboxHandler
{
    private static readonly Dictionary<string, string> TemplateFor = new(StringComparer.Ordinal)
    {
        [BookMealHandler.RequestedEvent] = "BOOKING_REQUESTED",
        [BookMealHandler.ConfirmedEvent] = "BOOKING_CONFIRMED",
        [BookingDecisionHandler.ApprovedEvent] = "BOOKING_APPROVED",
        [BookingDecisionHandler.RejectedEvent] = "BOOKING_REJECTED",
        [BookingDecisionHandler.CancelledEvent] = "BOOKING_CANCELLED",
    };

    public IReadOnlyCollection<string> EventTypes => TemplateFor.Keys;

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { BookingNo: { Length: > 0 } bookingNo })
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) has no booking number.");
        }

        if (message.EventType == BookingDecisionHandler.CancelledEvent && payload.ByDonor)
        {
            return;
        }

        // A booking is never deleted, so a missing one is an error to retry and log, not to skip.
        var booking = await queries.GetAsync(bookingNo, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Outbox message {message.Id} names booking {bookingNo}, which does not exist.");

        if (booking.DonorUserId is not { } donor)
        {
            return;
        }

        var template = TemplateFor[message.EventType];
        await texts.SendAsync(donor, template, BookingTexts.Values(booking), $"{template}:{bookingNo}", cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(string? BookingNo, bool ByDonor);
}

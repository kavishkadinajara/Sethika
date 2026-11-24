using System.Globalization;
using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Notifications;

namespace Sethika.Application.Bookings.Recurring;

/// <summary>
/// Texts the donor when the yearly job could not make a year's booking (plan 07, H4): every place was
/// taken, or the meal was not open that day. A year that was booked is texted like any other booking.
/// </summary>
/// <remarks>
/// The dedup key is the template, the yearly booking and the year: a year is tried once, so its text
/// goes once, however often the message is processed. The institution gets no text (C11).
/// </remarks>
public sealed class RecurringBookingSmsHandler(IBookingNotificationQueries queries, IUserTextMessages texts) : IOutboxHandler
{
    public const string FullTemplate = "RECURRING_FULL";
    public const string ClosedTemplate = "RECURRING_CLOSED";

    public IReadOnlyCollection<string> EventTypes { get; } = [BookMealHandler.YearNotBookedEvent];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { Recurring: var recurring, Year: > 0, Date: { Length: > 0 } dateText } || recurring == Guid.Empty
            || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) does not name a yearly booking and a day.");
        }

        // A yearly booking is never deleted, so a missing one is an error to retry and log.
        var details = await queries.GetRecurringAsync(recurring, date, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Outbox message {message.Id} names yearly booking {recurring}, which does not exist.");

        var template = payload.Reason == BookMealHandler.FullReason ? FullTemplate : ClosedTemplate;
        await texts
            .SendAsync(details.DonorUserId!.Value, template, BookingTexts.Values(details), $"{template}:{recurring}:{payload.Year}", cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record Payload(Guid Recurring, int Year, string? Date, string? Reason);
}

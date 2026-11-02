using System.Globalization;
using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;

namespace Sethika.Application.Bookings.Live;

/// <summary>
/// The outbox message a change of an institution's open days writes in its own transaction (plan 07,
/// H10): a place taken or given back, a slot added, changed or switched off, a day blocked or opened.
/// </summary>
/// <remarks>
/// A message of its own, not a second handler on the booking events: the outbox gives each event type
/// one handler, so a failed hint can never make a booking's text be sent again. A range rather than a
/// list of days, so a block of weeks is one small message; no range means every day, as when a slot
/// changes.
/// </remarks>
public static class AvailabilityChange
{
    public const string Event = "AvailabilityChanged";

    /// <summary>The message for <paramref name="institutionId"/>'s days <paramref name="from"/> to <paramref name="to"/>, or every day when both are <see langword="null"/>.</summary>
    public static OutboxMessage Message(int institutionId, DateOnly? from, DateOnly? to, DateTime now) =>
        new()
        {
            EventType = Event,
            AggregateType = "Institution",
            AggregateId = institutionId.ToString(CultureInfo.InvariantCulture),
            Payload = JsonSerializer.Serialize(new { institutionId, from = Iso(from), to = Iso(to) }),
            OccurredAt = now,
        };

    internal static string? Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Sends the hint for an <see cref="AvailabilityChange"/> to the institution's open calendars.</summary>
/// <remarks>
/// The institution is named by its id in the message and by its slug to the calendars, which watch the
/// page they show. An institution is never deleted, so a missing one is an error to retry and log.
/// </remarks>
public sealed class AvailabilityChangedHandler(IDonorBookingQueries queries, IAvailabilityNotifier notifier) : IOutboxHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [AvailabilityChange.Event];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { InstitutionId: > 0 } || !TryDate(payload.From, out var from) || !TryDate(payload.To, out var to))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) does not name an institution and its days.");
        }

        var slug = await queries.SlugOfAsync(payload.InstitutionId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Outbox message {message.Id} names institution {payload.InstitutionId}, which does not exist.");

        await notifier.ChangedAsync(slug, from, to, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryDate(string? text, out DateOnly? date)
    {
        date = null;
        if (text is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }

    private sealed record Payload(int InstitutionId, string? From, string? To);
}

using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Slots;

namespace Sethika.Application.Bookings.Discovery;

/// <summary>
/// Keeps the geo index current (plan 06, G5): when an institution is listed or unlisted, puts it
/// where SQL Server says it is now.
/// </summary>
/// <remarks>
/// It reads the institution's state when it runs, not the state the message was written in, so
/// running it twice, late, or out of order with another message for the same institution still
/// leaves the index right. A failure retries with the outbox; the nightly rebuild catches anything
/// the retries could not.
/// </remarks>
public sealed class InstitutionGeoHandler(IInstitutionGeoIndex geoIndex) : IOutboxHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [SlotManagementHandler.ListingChangedEvent];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { InstitutionId: > 0 })
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) names no institution.");
        }

        await geoIndex.RefreshAsync(payload.InstitutionId, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(int InstitutionId);
}

using System.Text.Json;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// Texts the donor when their request is received, approved or rejected, and a leader when they are
/// assigned or taken off (Phase E, 8.8, V14). A cancellation is texted by
/// <see cref="AppointmentCancelledHandler"/>, which also releases the time (V27).
/// </summary>
/// <remarks>
/// <para>
/// The institution gets no texts, as with bookings: a decline shows on its agenda as "needs leaders",
/// so <c>LeaderDeclined</c> has no handler and is marked processed.
/// </para>
/// <para>
/// A donor's text is keyed by template and appointment: each of those moves happens to an
/// appointment once. A leader can be assigned, decline and be assigned again, so a leader's text is
/// keyed by the outbox message as well: a re-run of one message texts nobody twice, and a second
/// assignment is still told.
/// </para>
/// </remarks>
public sealed class AppointmentSmsHandler(IAppointmentNotificationQueries queries, AppointmentTexts texts) : IOutboxHandler
{
    private static readonly Dictionary<string, string> DonorTemplate = new(StringComparer.Ordinal)
    {
        [RequestServiceHandler.RequestedEvent] = AppointmentTexts.Requested,
        [AssignLeadersHandler.ApprovedEvent] = AppointmentTexts.Approved,
        [AppointmentDecisionHandler.RejectedEvent] = AppointmentTexts.Rejected,
    };

    private static readonly Dictionary<string, string> LeaderTemplate = new(StringComparer.Ordinal)
    {
        [AssignLeadersHandler.AssignedEvent] = AppointmentTexts.LeaderAssigned,
        [AppointmentDecisionHandler.RemovedEvent] = AppointmentTexts.LeaderRemoved,
    };

    public IReadOnlyCollection<string> EventTypes { get; } =
        [.. DonorTemplate.Keys, .. LeaderTemplate.Keys];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { AppointmentNo: { Length: > 0 } appointmentNo })
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) has no appointment number.");
        }

        // An appointment is never deleted, so a missing one is an error to retry and log, not to skip.
        var appointment = await queries.GetAsync(appointmentNo, cancellationToken).ConfigureAwait(false)
                          ?? throw new InvalidOperationException($"Outbox message {message.Id} names appointment {appointmentNo}, which does not exist.");

        if (DonorTemplate.TryGetValue(message.EventType, out var donorTemplate))
        {
            await texts.ToDonorAsync(appointment, donorTemplate, $"{donorTemplate}:{appointmentNo}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var template = LeaderTemplate[message.EventType];
        if (payload.LeaderId is not { } leaderId)
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) names no leader.");
        }

        var leaders = await queries.LeadersAsync(appointmentNo, cancellationToken).ConfigureAwait(false);
        var leader = leaders.SingleOrDefault(l => l.LeaderId == leaderId)
                     ?? throw new InvalidOperationException($"Outbox message {message.Id} names leader {leaderId}, who is not on appointment {appointmentNo}.");

        await texts.ToLeaderAsync(appointment, leader, template, $"{template}:{appointmentNo}:{leaderId}:{message.Id}", cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(string? AppointmentNo, int? LeaderId);
}

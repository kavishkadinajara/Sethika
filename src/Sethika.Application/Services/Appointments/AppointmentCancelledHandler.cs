using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// Releases a cancelled appointment's leader time (V16): deletes its blocks from
/// <c>booking.LeaderTimeBlocks</c>, so the leaders can be assigned elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// A donor cancels in a personal session, which Row-Level Security keeps away from leaders, their
/// assignments and their blocks (V9), so the release cannot happen in the donor's transaction. The
/// outbox runs in a platform session and can. Until it runs, seconds later, the leaders look busy:
/// a stale block can only refuse an assignment, never allow a double one.
/// </para>
/// <para>
/// Idempotent, as every outbox handler must be: a second run finds nothing to delete. Only a
/// cancelled appointment's blocks are deleted, so a message can never free the time of one that
/// is still going. The institution's own cancellation (8.7) deletes the blocks in its transaction;
/// its message then finds none.
/// </para>
/// </remarks>
public sealed class AppointmentCancelledHandler(ISethikaDbContext db) : IOutboxHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [AppointmentDecisionHandler.CancelledEvent];

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { AppointmentNo: { Length: > 0 } appointmentNo })
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) has no appointment number.");
        }

        // An appointment is never deleted, so a missing one is an error to retry and log, not to skip.
        var appointment = await db.ServiceAppointments
            .AsNoTracking()
            .Where(a => a.AppointmentNo == appointmentNo)
            .Select(a => new { a.Id, a.Status })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} names appointment {appointmentNo}, which does not exist.");

        if (appointment.Status != AppointmentStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} asks to release appointment {appointmentNo}, which is {appointment.Status}, not cancelled.");
        }

        await db.LeaderTimeBlocks
            .Where(b => b.AppointmentId == appointment.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record Payload(string? AppointmentNo);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// Moves an appointment through its life after it was asked for. For now the donor's own
/// cancellation (Phase E, 8.5); the institution's decisions follow in 8.7.
/// </summary>
/// <remarks>
/// Row-Level Security shows a donor their own appointments through the owner branch (V9), and a
/// staff member who asked for a service elsewhere sees that one too. So a donor's cancellation also
/// checks the appointment is theirs; anything else reads as not found.
/// </remarks>
public sealed class AppointmentDecisionHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    /// <summary>Written on every cancellation; its handler releases the leaders' time (V16).</summary>
    public const string CancelledEvent = "AppointmentCancelled";

    /// <summary>
    /// The donor cancels their own request or appointment, until the end of the day before (V13).
    /// </summary>
    /// <remarks>
    /// The leaders' blocks are not touched here: the donor's session cannot see them (V9), so a
    /// DELETE would match nothing. <see cref="AppointmentCancelledHandler"/> releases them from the
    /// outbox, in a platform session (V16).
    /// </remarks>
    public async Task<AppointmentResult> CancelAsDonorAsync(
        string appointmentNo,
        string? reason,
        Guid donorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentNo))
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        var appointment = await db.ServiceAppointments
            .SingleOrDefaultAsync(a => a.AppointmentNo == appointmentNo.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null || appointment.DonorUserId != donorId)
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var result = appointment.CancelAsDonor(reason, donorId, now);
        if (!result.Succeeded)
        {
            return AppointmentResult.Failed(result.Error!);
        }

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = CancelledEvent,
            AggregateType = nameof(ServiceAppointment),
            AggregateId = appointment.AppointmentNo,
            Payload = JsonSerializer.Serialize(new { appointmentNo = appointment.AppointmentNo, donorUserId = donorId, byDonor = true }),
            OccurredAt = now,
        });

        // One save: the appointment, its history row and the outbox message commit together.
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppointmentResult.Failed(AppointmentResult.Stale);
        }

        return AppointmentResult.Ok(appointment.Status);
    }
}

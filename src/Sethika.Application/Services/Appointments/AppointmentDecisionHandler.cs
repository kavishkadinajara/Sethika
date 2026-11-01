using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>What the institution does to an appointment after leaders were assigned or not (Phase E, 8.7).</summary>
public enum AppointmentDecision
{
    Reject,
    Cancel,
    Complete,
}

/// <summary>
/// Moves an appointment through its life after it was asked for: the institution's decisions, a
/// leader's answer and removal, and the donor's own cancellation (Phase E, section 4, flows 2-5).
/// Assigning leaders is <see cref="AssignLeadersHandler"/>, which needs the leaders' locks.
/// </summary>
/// <remarks>
/// <para>
/// Every change frees time and never takes any, so no lock is needed: a block deleted a moment early
/// can only let an assignment through that is genuinely free. The blocks that stop being held - a
/// leader removed or declining, every leader of a rejected or cancelled appointment - are deleted
/// in the same transaction as the appointment's save, so a change that loses on <c>RowVersion</c>
/// frees nothing.
/// </para>
/// <para>
/// The appointment's row is always written, even when only a leader's row changes, so that two
/// changes to one appointment meet on its <c>RowVersion</c>, as in <see cref="AssignLeadersHandler"/>.
/// </para>
/// <para>
/// Row-Level Security shows staff their institution's appointments and also any they asked for
/// themselves elsewhere; a donor sees only their own. So each change also checks the appointment
/// belongs to the institution acted for, or to the donor; anything else reads as not found.
/// </para>
/// </remarks>
public sealed class AppointmentDecisionHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    /// <summary>Written on every cancellation; its handler releases the leaders' time (V16).</summary>
    public const string CancelledEvent = "AppointmentCancelled";

    /// <summary>Written when a request is rejected: the donor is told.</summary>
    public const string RejectedEvent = "AppointmentRejected";

    /// <summary>Written when a leader declines: the institution needs another (V6).</summary>
    public const string DeclinedEvent = "LeaderDeclined";

    /// <summary>Written when a leader is taken off an appointment: the leader is told.</summary>
    public const string RemovedEvent = "LeaderRemoved";

    // ------------------------------------------------------------------ the institution

    /// <summary>A decision by staff of <paramref name="institutionId"/>, the institution they act for.</summary>
    public Task<AppointmentResult> DecideAsync(
        int institutionId,
        string appointmentNo,
        AppointmentDecision decision,
        string? reason,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            appointmentNo,
            a => a.InstitutionId == institutionId,
            (a, now) => decision switch
            {
                AppointmentDecision.Reject => a.Reject(reason ?? string.Empty, userId, now),
                AppointmentDecision.Cancel => a.CancelAsInstitution(reason ?? string.Empty, userId, now),
                AppointmentDecision.Complete => a.Complete(userId, now),
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            },
            releaseHere: true,
            byDonor: false,
            leaderId: null,
            cancellationToken);

    /// <summary>Takes the leader <paramref name="leader"/> (their public id) off the appointment and frees their time.</summary>
    public async Task<AppointmentResult> RemoveLeaderAsync(
        int institutionId,
        string appointmentNo,
        Guid leader,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var leaderId = await RosterIdAsync(institutionId, leader, cancellationToken).ConfigureAwait(false);
        return leaderId is null
            ? AppointmentResult.Failed(ServiceErrors.NotAssigned)
            : await ChangeAsync(
                appointmentNo,
                a => a.InstitutionId == institutionId,
                (a, now) => a.Remove(leaderId.Value, userId, now),
                releaseHere: true,
                byDonor: false,
                leaderId,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// An administrator answers for the leader <paramref name="leader"/>, who may never sign in (V1):
    /// <c>RespondedOnBehalf</c>.
    /// </summary>
    public async Task<AppointmentResult> RespondOnBehalfAsync(
        int institutionId,
        string appointmentNo,
        Guid leader,
        bool accept,
        string? reason,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var leaderId = await RosterIdAsync(institutionId, leader, cancellationToken).ConfigureAwait(false);
        return leaderId is null
            ? AppointmentResult.Failed(ServiceErrors.NotAssigned)
            : await RespondAsync(institutionId, appointmentNo, leaderId.Value, accept, reason, userId, onBehalf: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The signed-in leader answers for themselves, in the institution session whose roster links
    /// their account (V1, V21).
    /// </summary>
    public async Task<AppointmentResult> RespondAsLeaderAsync(
        int institutionId,
        string appointmentNo,
        bool accept,
        string? reason,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var leaderId = await db.ReligiousLeaders
            .Where(l => l.UserId == userId && l.InstitutionId == institutionId && l.IsActive)
            .Select(l => (int?)l.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return leaderId is null
            ? AppointmentResult.Failed(AppointmentResult.NotALeader)
            : await RespondAsync(institutionId, appointmentNo, leaderId.Value, accept, reason, userId, onBehalf: false, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ the donor

    /// <summary>
    /// The donor cancels their own request or appointment, until the end of the day before (V13).
    /// </summary>
    /// <remarks>
    /// The leaders' blocks are not touched here: the donor's session cannot see them (V9), so a
    /// DELETE would match nothing. <see cref="AppointmentCancelledHandler"/> releases them from the
    /// outbox, in a platform session (V16).
    /// </remarks>
    public Task<AppointmentResult> CancelAsDonorAsync(
        string appointmentNo,
        string? reason,
        Guid donorId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(
            appointmentNo,
            a => a.DonorUserId == donorId,
            (a, now) => a.CancelAsDonor(reason, donorId, now),
            releaseHere: false,
            byDonor: true,
            leaderId: null,
            cancellationToken);

    // ------------------------------------------------------------------ plumbing

    private Task<AppointmentResult> RespondAsync(
        int institutionId,
        string appointmentNo,
        int leaderId,
        bool accept,
        string? reason,
        Guid userId,
        bool onBehalf,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            appointmentNo,
            a => a.InstitutionId == institutionId,
            (a, now) => a.Respond(leaderId, accept, reason, userId, onBehalf, now),
            releaseHere: true,
            byDonor: false,
            leaderId,
            cancellationToken);

    /// <summary>The internal id of a leader on the institution's roster, active or not.</summary>
    private Task<int?> RosterIdAsync(int institutionId, Guid leader, CancellationToken cancellationToken) =>
        db.ReligiousLeaders
            .Where(l => l.PublicId == leader && l.InstitutionId == institutionId)
            .Select(l => (int?)l.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<AppointmentResult> ChangeAsync(
        string appointmentNo,
        Func<ServiceAppointment, bool> mayAct,
        Func<ServiceAppointment, DateTime, ServiceResult> transition,
        bool releaseHere,
        bool byDonor,
        int? leaderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appointmentNo))
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A donor's session sees none of the assignments (V9), so the collection is empty for them.
        var appointment = await db.ServiceAppointments
            .Include(a => a.Leaders)
            .SingleOrDefaultAsync(a => a.AppointmentNo == appointmentNo.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null || !mayAct(appointment))
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var liveBefore = appointment.LiveLeaders.Select(l => l.LeaderId).ToList();
        var statusBefore = appointment.Status;

        var result = transition(appointment, now);
        if (!result.Succeeded)
        {
            return AppointmentResult.Failed(result.Error!);
        }

        var released = liveBefore.Except(appointment.LiveLeaders.Select(l => l.LeaderId)).ToList();
        var ended = appointment.Status is AppointmentStatus.Rejected or AppointmentStatus.Cancelled;

        db.Entry(appointment).Property(a => a.Status).IsModified = true;

        foreach (var eventType in EventsFor(appointment, statusBefore, released, leaderId))
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = eventType,
                AggregateType = nameof(ServiceAppointment),
                AggregateId = appointment.AppointmentNo,
                Payload = JsonSerializer.Serialize(new { appointmentNo = appointment.AppointmentNo, donorUserId = appointment.DonorUserId, byDonor, leaderId }),
                OccurredAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppointmentResult.Failed(AppointmentResult.Stale);
        }

        // After the save: a change that loses on RowVersion stops there and never touches the blocks.
        // (Its transaction would roll a delete back anyway; this only spares the locks.)
        if (releaseHere && (ended || released.Count > 0))
        {
            var id = appointment.Id;
            await db.LeaderTimeBlocks
                .Where(b => b.AppointmentId == id && (ended || released.Contains(b.LeaderId)))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return AppointmentResult.Ok(appointment.Status);
    }

    private static IEnumerable<string> EventsFor(ServiceAppointment appointment, string statusBefore, List<int> released, int? leaderId)
    {
        if (appointment.Status != statusBefore)
        {
            switch (appointment.Status)
            {
                case AppointmentStatus.Rejected:
                    yield return RejectedEvent;
                    break;
                case AppointmentStatus.Cancelled:
                    yield return CancelledEvent;
                    break;
            }

            yield break;
        }

        if (leaderId is { } id && released.Contains(id))
        {
            var row = appointment.Leaders.Single(l => l.LeaderId == id);
            yield return row.Response == AssignmentResponse.Declined ? DeclinedEvent : RemovedEvent;
        }
    }
}

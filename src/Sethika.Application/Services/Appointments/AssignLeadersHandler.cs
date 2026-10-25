using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Core;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// Assigns religious leaders to an appointment, which approves a request (Phase E, section 4, flow 2):
/// the second concurrency pattern, an interval that must not overlap, beside the booking engine's
/// counted inventory.
/// </summary>
/// <remarks>
/// Three layers, as in the booking engine (plan 03, C2):
/// <list type="number">
/// <item>The leaders' Redis locks (<see cref="ILeaderLocks"/>, V3), in ascending id order. A loser
/// gets a quick 409 to try again. If Redis is down, the assignment goes on without them.</item>
/// <item>Inside the lock, a read of the leaders' blocks in the time asked for, only to name who is not
/// free.</item>
/// <item><c>PK_LeaderTimeBlocks</c>, the guarantee (V2): each leader's 15-minute blocks are inserted
/// in the same transaction, and a block that already exists is a 2627, whatever layers 1 and 2 did.</item>
/// </list>
/// The appointment's row is always updated, even when it is already approved and only a leader is
/// added, so that two assignments to one appointment meet on its <c>RowVersion</c>: otherwise both
/// could pass the leaders-required check on the same stale count.
/// </remarks>
public sealed class AssignLeadersHandler(ISethikaDbContext db, ILeaderLocks locks, TimeProvider timeProvider)
{
    /// <summary>Written once per leader assigned: the leader is told (V14).</summary>
    public const string AssignedEvent = "LeaderAssigned";

    /// <summary>Written when the assignment approves a request: the donor is told.</summary>
    public const string ApprovedEvent = "AppointmentApproved";

    private const string BlocksKey = "PK_LeaderTimeBlocks";

    /// <summary>Assigns <paramref name="leaders"/> (their public ids) for staff of <paramref name="institutionId"/>.</summary>
    public async Task<AppointmentResult> AssignAsync(
        int institutionId,
        string appointmentNo,
        IReadOnlyCollection<Guid> leaders,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leaders);

        if (string.IsNullOrWhiteSpace(appointmentNo))
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        if (leaders.Count == 0)
        {
            return AppointmentResult.Failed(AppointmentResult.InvalidDetails);
        }

        var chosen = await db.ReligiousLeaders
            .AsNoTracking()
            .Where(l => leaders.Contains(l.PublicId) && l.InstitutionId == institutionId && l.IsActive)
            .Select(l => new { l.Id, l.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chosen.Count != leaders.Distinct().Count())
        {
            return AppointmentResult.Failed(ServiceErrors.LeaderUnavailable);
        }

        var ids = chosen.Select(l => l.Id).ToList();

        await using var lease = await locks.AcquireAsync(ids, cancellationToken).ConfigureAwait(false);
        if (lease.Outcome == LockOutcome.Busy)
        {
            return AppointmentResult.Failed(ServiceErrors.TryAgain);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.ServiceAppointments
            .Include(a => a.Leaders)
            .SingleOrDefaultAsync(a => a.AppointmentNo == appointmentNo.Trim(), cancellationToken)
            .ConfigureAwait(false);

        // RLS shows staff their institution's appointments, and also any they asked for themselves as
        // a donor elsewhere; only their own institution's may be acted on.
        if (appointment is null || appointment.InstitutionId != institutionId)
        {
            return AppointmentResult.Failed(AppointmentResult.NotFound);
        }

        var type = await db.ServiceTypes
            .AsNoTracking()
            .SingleAsync(t => t.Id == appointment.ServiceTypeId, cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var wasRequested = appointment.Status == AppointmentStatus.Requested;

        var result = appointment.Assign(ids, type.LeadersRequired, userId, now);
        if (!result.Succeeded)
        {
            return AppointmentResult.Failed(result.Error!);
        }

        var blocks = TimeBlocks.For(appointment.StartsAt, appointment.EndsAt, type.BufferFor(appointment.LocationMode));
        var from = blocks[0];
        var to = blocks[^1];

        var busy = await db.LeaderTimeBlocks
            .AsNoTracking()
            .Where(b => ids.Contains(b.LeaderId) && b.BlockStart >= from && b.BlockStart <= to)
            .Select(b => b.LeaderId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (busy.Count > 0)
        {
            return AppointmentResult.Failed(ServiceErrors.LeaderNotFree, chosen.Where(l => busy.Contains(l.Id)).Select(l => l.DisplayName).ToList());
        }

        db.Entry(appointment).Property(a => a.Status).IsModified = true;

        foreach (var leader in chosen)
        {
            db.OutboxMessages.Add(Message(AssignedEvent, appointment, now, leaderId: leader.Id));
        }

        if (wasRequested)
        {
            db.OutboxMessages.Add(Message(ApprovedEvent, appointment, now, leaderId: null));
        }

        try
        {
            // The assignments first: every block names its assignment (FK_LeaderTimeBlocks_Assignment).
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var leaderId in ids)
            {
                db.LeaderTimeBlocks.AddRange(blocks.Select(start => new LeaderTimeBlock
                {
                    LeaderId = leaderId,
                    BlockStart = start,
                    InstitutionId = institutionId,
                    Kind = LeaderTimeBlockKind.Appointment,
                    AppointmentId = appointment.Id,
                }));
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == BlocksKey)
        {
            // Another assignment or leave took some of this time after the read above: only possible
            // when the lock was not held. The database refused it; who it was is not known here.
            return AppointmentResult.Failed(ServiceErrors.LeaderNotFree);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppointmentResult.Failed(AppointmentResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return AppointmentResult.Ok(appointment.Status);
    }

    private static OutboxMessage Message(string eventType, ServiceAppointment appointment, DateTime now, int? leaderId) => new()
    {
        EventType = eventType,
        AggregateType = nameof(ServiceAppointment),
        AggregateId = appointment.AppointmentNo,
        Payload = JsonSerializer.Serialize(new { appointmentNo = appointment.AppointmentNo, leaderId }),
        OccurredAt = now,
    };
}

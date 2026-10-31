using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Institutions;

namespace Sethika.Application.Access.Roles;

/// <summary>
/// Staff invitations (plan 04, X1-X5): bringing a person who is not yet a member into the
/// institution with one of its roles. Inviting obeys the assign rules (rank, never oneself);
/// accepting is the invitee's own act, from their own session.
/// </summary>
/// <remarks>
/// Accepting, declining and cancelling each move the row with a conditional UPDATE (status still
/// Pending, not yet expired), so of an accept and a cancel that race, exactly one happens: the
/// domain method says whether the move is allowed, the UPDATE is what makes it happen once.
/// </remarks>
public sealed partial class RoleManagementHandler
{
    private const string ActiveStatus = "Active";
    private const string PendingIndex = "UX_Invite_Pending";

    /// <summary>Invites the account with that phone number to hold a role more junior than the actor's.</summary>
    public async Task<RoleChangeResult> InviteAsync(RoleActor actor, InviteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);

        if (!phoneNumbers.TryNormalize(command.Phone, out var phone))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvalidPhone);
        }

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.PhoneNumber == phone && !u.IsDeleted && u.Status == ActiveStatus)
            .Select(u => new { u.Id, u.FullName })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.AccountNotFound);
        }

        if (account.Id == actor.UserId)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.OwnRoles);
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindRoleAsync(actor, command.Role ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleNotFound);
        }

        var refusal = await CheckRankAsync(actor, role.Rank, at, cancellationToken).ConfigureAwait(false)
                      ?? await CheckRoleHeldAsync(actor, role, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await ActiveAssignments(account.Id, actor.InstitutionId, at).AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.AlreadyMember);
        }

        // One pending row per person and institution (UX_Invite_Pending). A pending row past its
        // time is closed as expired so this one can take its place; an open one is a refusal.
        var waiting = await db.StaffInvitations
            .SingleOrDefaultAsync(
                i => i.InstitutionId == actor.InstitutionId && i.InviteeUserId == account.Id && i.Status == InvitationStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

        if (waiting is not null && !waiting.Expire(at))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationPending);
        }

        var invitation = StaffInvitation.Invite(
            actor.InstitutionId,
            role.Id,
            account.Id,
            account.FullName,
            phone,
            // For a texted link in a later slice; nothing here ever reads the token itself.
            Convert.ToHexStringLower(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
            actor.UserId,
            at);

        db.StaffInvitations.Add(invitation);
        Record(actor, role.Id, role.Code, "Invited", account.Id, at);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == PendingIndex)
        {
            // Another invitation to the same person was saved first.
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationPending);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RoleChangeResult.Invited(invitation.PublicId);
    }

    /// <summary>Cancels an open invitation of the actor's institution, for a role more junior than theirs.</summary>
    public async Task<RoleChangeResult> CancelInvitationAsync(RoleActor actor, Guid invitationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var invitation = await db.StaffInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.PublicId == invitationId && i.InstitutionId == actor.InstitutionId, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationNotFound);
        }

        var role = await db.Roles
            .Where(r => r.Id == invitation.RoleId)
            .Join(db.RoleLevels, r => r.RoleLevelId, l => l.Id, (r, l) => new { r.Code, l.Rank })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var refusal = await CheckRankAsync(actor, role.Rank, at, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Refused(invitation.Revoke(actor.UserId, at)) is { } closed)
        {
            return closed;
        }

        if (!await ClaimAsync(invitation.Id, at, InvitationStatus.Revoked, actor.UserId, cancellationToken).ConfigureAwait(false))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationClosed);
        }

        Record(actor, invitation.RoleId, role.Code, "InvitationCancelled", invitation.InviteeUserId, at);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RoleChangeResult.Success;
    }

    /// <summary>
    /// The invitee accepts: the role is theirs from this moment (X4). Called from their own session,
    /// whatever institution it acts for; RLS shows them their own invitations and no one else's.
    /// </summary>
    public async Task<RoleChangeResult> AcceptInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var invitation = await FindOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationNotFound);
        }

        if (Refused(invitation.Accept(userId, at)) is { } refused)
        {
            return refused;
        }

        // A role switched off since the invitation was sent grants nothing; the invitation stays
        // open for the institution to cancel or the person to decline.
        var role = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == invitation.RoleId && r.InstitutionId == invitation.InstitutionId && r.IsActive)
            .Select(r => new { r.Id, r.Code })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleNotFound);
        }

        if (!await ClaimAsync(invitation.Id, at, InvitationStatus.Accepted, userId, cancellationToken).ConfigureAwait(false))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationClosed);
        }

        // As AssignAsync: an active row can only be one whose window lapsed, and is reopened.
        var existing = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = role.Id,
                InstitutionId = invitation.InstitutionId,
                AssignedBy = invitation.InvitedBy,
                IsActive = true,
            });
        }
        else
        {
            existing.ValidFrom = null;
            existing.ValidTo = null;
        }

        await BumpUserVersionAsync(userId, cancellationToken).ConfigureAwait(false);
        Record(new RoleActor(userId, invitation.InstitutionId), role.Id, role.Code, "InvitationAccepted", userId, at);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RoleChangeResult.Success;
    }

    /// <summary>The invitee says no. Nothing else changes.</summary>
    public async Task<RoleChangeResult> DeclineInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var at = timeProvider.GetUtcNow().UtcDateTime;

        var invitation = await FindOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvitationNotFound);
        }

        if (Refused(invitation.Decline(userId, at)) is { } refused)
        {
            return refused;
        }

        return await ClaimAsync(invitation.Id, at, InvitationStatus.Declined, userId, cancellationToken).ConfigureAwait(false)
            ? RoleChangeResult.Success
            : RoleChangeResult.Failed(RoleChangeFailure.InvitationClosed);
    }

    /// <summary>
    /// The caller's own invitation, or null. Matched on the invitee as well as RLS: a session acting
    /// for the inviting institution can see the row, but it is still not theirs to answer.
    /// </summary>
    private Task<StaffInvitation?> FindOwnInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken) =>
        db.StaffInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.PublicId == invitationId && i.InviteeUserId == userId, cancellationToken);

    /// <summary>
    /// Moves the row out of Pending if it is still pending and open, and says whether it did. The
    /// one place the move is decided when two requests race.
    /// </summary>
    private async Task<bool> ClaimAsync(int id, DateTime at, string status, Guid by, CancellationToken cancellationToken)
    {
        var open = db.StaffInvitations.Where(i => i.Id == id && i.Status == InvitationStatus.Pending && i.ExpiresAt > at);

        var moved = status == InvitationStatus.Accepted
            ? await open.ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.Status, status)
                          .SetProperty(i => i.AcceptedUserId, (Guid?)by)
                          .SetProperty(i => i.AcceptedAt, (DateTime?)at),
                    cancellationToken)
                .ConfigureAwait(false)
            : await open.ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.Status, status)
                          .SetProperty(i => i.ClosedBy, (Guid?)by)
                          .SetProperty(i => i.ClosedAt, (DateTime?)at),
                    cancellationToken)
                .ConfigureAwait(false);

        return moved == 1;
    }

    private static RoleChangeResult? Refused(InvitationResult result) => result.Error switch
    {
        null => null,
        InvitationErrors.Expired => RoleChangeResult.Failed(RoleChangeFailure.InvitationExpired),
        InvitationErrors.NotPending => RoleChangeResult.Failed(RoleChangeFailure.InvitationClosed),
        _ => RoleChangeResult.Failed(RoleChangeFailure.InvitationNotFound),
    };
}

namespace Sethika.Domain.Institutions;

/// <summary>
/// A person invited onto an institution's staff with one of its roles (plan 04, X1-X5). Maps
/// <c>org.StaffInvitations</c>.
/// </summary>
/// <remarks>
/// Always addressed to an existing account (<see cref="InviteeUserId"/>), so the invitee answers
/// it from their own session; RLS shows it to the institution and to them. It is open while
/// <see cref="InvitationStatus.Pending"/> and before <see cref="ExpiresAt"/>; expiry is read from
/// the clock, never written by a job (X5). Who may invite whom with which role is the handler's
/// to check (the role rules of plan 01, 5.9); this only keeps the state machine honest.
/// </remarks>
public class StaffInvitation
{
    /// <summary>How long an invitation stays open (X5).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public int Id { get; private set; }
    public Guid PublicId { get; private set; }
    public int InstitutionId { get; private set; }
    public Guid InviteeUserId { get; private set; }
    public string InviteeName { get; private set; } = string.Empty;
    public string? InviteePhone { get; private set; }
    public string? InviteeEmail { get; private set; }
    public int RoleId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = InvitationStatus.Pending;
    public Guid InvitedBy { get; private set; }
    public DateTime InvitedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Guid? AcceptedUserId { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>A new, pending invitation. The caller has already found the account by phone.</summary>
    public static StaffInvitation Invite(
        int institutionId,
        int roleId,
        Guid inviteeUserId,
        string inviteeName,
        string inviteePhone,
        string tokenHash,
        Guid invitedBy,
        DateTime now) => new()
        {
            PublicId = Guid.NewGuid(),
            InstitutionId = institutionId,
            RoleId = roleId,
            InviteeUserId = inviteeUserId,
            InviteeName = inviteeName,
            InviteePhone = inviteePhone,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            InvitedBy = invitedBy,
            InvitedAt = now,
            ExpiresAt = now + Lifetime,
        };

    /// <summary>Pending and not yet expired.</summary>
    public bool IsOpen(DateTime now) => Status == InvitationStatus.Pending && ExpiresAt > now;

    /// <summary>Pending → Accepted, by the person invited and nobody else, while it is open.</summary>
    public InvitationResult Accept(Guid userId, DateTime now)
    {
        if (userId != InviteeUserId)
        {
            return InvitationResult.Fail(InvitationErrors.NotYours);
        }

        if (Close(now) is { } refused)
        {
            return refused;
        }

        Status = InvitationStatus.Accepted;
        AcceptedUserId = userId;
        AcceptedAt = now;
        return InvitationResult.Ok;
    }

    /// <summary>Pending → Declined, by the person invited, while it is open.</summary>
    public InvitationResult Decline(Guid userId, DateTime now)
    {
        if (userId != InviteeUserId)
        {
            return InvitationResult.Fail(InvitationErrors.NotYours);
        }

        if (Close(now) is { } refused)
        {
            return refused;
        }

        Status = InvitationStatus.Declined;
        ClosedBy = userId;
        ClosedAt = now;
        return InvitationResult.Ok;
    }

    /// <summary>Pending → Revoked, by the institution, while it is open.</summary>
    public InvitationResult Revoke(Guid userId, DateTime now)
    {
        if (Close(now) is { } refused)
        {
            return refused;
        }

        Status = InvitationStatus.Revoked;
        ClosedBy = userId;
        ClosedAt = now;
        return InvitationResult.Ok;
    }

    /// <summary>
    /// Pending but past <see cref="ExpiresAt"/> → Expired. Only when a new invitation to the same
    /// person replaces it: <c>UX_Invite_Pending</c> allows one pending row per person and
    /// institution, and an expired one still says Pending until this is written.
    /// </summary>
    public bool Expire(DateTime now)
    {
        if (Status != InvitationStatus.Pending || ExpiresAt > now)
        {
            return false;
        }

        Status = InvitationStatus.Expired;
        ClosedAt = now;
        return true;
    }

    /// <summary>Why an invitation cannot be answered now, or null when it can.</summary>
    private InvitationResult? Close(DateTime now) =>
        Status != InvitationStatus.Pending ? InvitationResult.Fail(InvitationErrors.NotPending)
        : ExpiresAt <= now ? InvitationResult.Fail(InvitationErrors.Expired)
        : null;
}

/// <summary><c>org.StaffInvitations.Status</c>.</summary>
public static class InvitationStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Revoked = "Revoked";

    /// <summary>
    /// Expiry is read from <c>ExpiresAt</c> (X5); this is written only when a new invitation
    /// replaces an expired one (<see cref="StaffInvitation.Expire"/>).
    /// </summary>
    public const string Expired = "Expired";
}

/// <summary>Why an invitation could not be answered. Each is also the API's error code.</summary>
public static class InvitationErrors
{
    public const string NotYours = "invitation_not_found";
    public const string NotPending = "invitation_closed";
    public const string Expired = "invitation_expired";
}

/// <summary>The outcome of a move on an invitation.</summary>
public readonly record struct InvitationResult(string? Error)
{
    public bool Succeeded => Error is null;

    public static InvitationResult Ok { get; }

    public static InvitationResult Fail(string error) => new(error);
}

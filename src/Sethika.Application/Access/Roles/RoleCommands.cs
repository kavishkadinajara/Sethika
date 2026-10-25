namespace Sethika.Application.Access.Roles;

/// <summary>Who is acting, and for which institution - both from the validated token.</summary>
/// <param name="UserId">The signed-in user.</param>
/// <param name="InstitutionId">The institution the session acts for.</param>
public sealed record RoleActor(Guid UserId, int InstitutionId);

/// <summary>A new institution role at a level, with its permissions.</summary>
/// <param name="Code">Unique in the institution; upper case letters, digits and underscores.</param>
/// <param name="Name">What people see.</param>
/// <param name="Level">The level's code, such as <c>STAFF</c>.</param>
/// <param name="Permissions">Permission codes. Each must be inside the level's ceiling and held by the actor.</param>
public sealed record CreateRoleCommand(string? Code, string? Name, string? Level, IReadOnlyCollection<string>? Permissions);

/// <summary>Why a role change was refused.</summary>
public enum RoleChangeFailure
{
    None = 0,

    InvalidCode = 1,
    InvalidName = 2,

    /// <summary>No active institution level has that code.</summary>
    UnknownLevel = 3,

    /// <summary>See <see cref="RoleChangeResult.Permissions"/> for which.</summary>
    UnknownPermission = 4,

    RoleExists = 5,
    RoleNotFound = 6,

    /// <summary>The user holds no role in this institution, so is not a member to assign to.</summary>
    MemberNotFound = 7,

    /// <summary>The user does not hold that role, so there is nothing to revoke.</summary>
    AssignmentNotFound = 8,

    /// <summary>The actor holds no role in this institution, so has no rank to act from.</summary>
    NoRank = 9,

    /// <summary>The role's level is not strictly more junior than the actor's most senior rank (A15).</summary>
    RankNotJunior = 10,

    /// <summary>Permissions outside the level's ceiling; see <see cref="RoleChangeResult.Permissions"/>.</summary>
    OutsideCeiling = 11,

    /// <summary>Permissions the actor does not hold; see <see cref="RoleChangeResult.Permissions"/>.</summary>
    NotHeld = 12,

    /// <summary>Nobody assigns or revokes their own roles.</summary>
    OwnRoles = 13,

    // ---- staff invitations (plan 04, X1-X5)

    /// <summary>Not a number that could be texted.</summary>
    InvalidPhone = 14,

    /// <summary>No active Sethika account has that phone number (X1).</summary>
    AccountNotFound = 15,

    /// <summary>They already hold a role here: give them another with assign, not an invitation (X3).</summary>
    AlreadyMember = 16,

    /// <summary>An open invitation to them is already waiting.</summary>
    InvitationPending = 17,

    /// <summary>No such invitation for this caller - including one addressed to someone else.</summary>
    InvitationNotFound = 18,

    /// <summary>Already accepted, declined or cancelled.</summary>
    InvitationClosed = 19,

    InvitationExpired = 20,
}

/// <summary>An invitation to an existing account, found by phone, to hold one of the institution's roles.</summary>
/// <param name="Phone">As typed; normalised to E.164 before the lookup.</param>
/// <param name="Role">The role's code in the institution.</param>
public sealed record InviteCommand(string? Phone, string? Role);

/// <summary>The outcome of a role change.</summary>
/// <param name="Failure">Why it was refused, or <see cref="RoleChangeFailure.None"/>.</param>
/// <param name="Permissions">The offending permission codes, for the refusals about permissions.</param>
/// <param name="Invitation">The new invitation's public id, after an invite.</param>
public sealed record RoleChangeResult(RoleChangeFailure Failure, IReadOnlyList<string> Permissions, Guid? Invitation = null)
{
    public bool Succeeded => Failure == RoleChangeFailure.None;

    public static RoleChangeResult Success { get; } = new(RoleChangeFailure.None, []);

    public static RoleChangeResult Invited(Guid invitation) => new(RoleChangeFailure.None, [], invitation);

    public static RoleChangeResult Failed(RoleChangeFailure failure, IReadOnlyList<string>? permissions = null) =>
        new(failure, permissions ?? []);
}

namespace Sethika.Application.Abstractions;

/// <summary>An institution role as the access screens list it (plan 04, X6).</summary>
/// <param name="Code">Unique in the institution.</param>
/// <param name="Name">What people see.</param>
/// <param name="Level">The level's code.</param>
/// <param name="LevelName">The level's name.</param>
/// <param name="Rank">1 is the most senior.</param>
/// <param name="IsSystem">Cloned from a template at approval (OWNER, MANAGER, STAFF, LEADER), not made here.</param>
/// <param name="CanManage">
/// The caller's rank is strictly more senior than the role's, so they may change it and hand it
/// out. The same rule the commands enforce; this only decides what the screen offers.
/// </param>
/// <param name="Members">People holding it now.</param>
/// <param name="Permissions">Its permission codes.</param>
public sealed record InstitutionRoleView(
    string Code,
    string Name,
    string Level,
    string LevelName,
    byte Rank,
    bool IsSystem,
    bool CanManage,
    int Members,
    IReadOnlyList<string> Permissions);

/// <summary>A permission in the role editor's matrix.</summary>
/// <param name="Code">Such as <c>BOOKINGS.CALENDAR.APPROVE</c>.</param>
/// <param name="Action">Its action, such as <c>APPROVE</c>.</param>
/// <param name="Name">What it lets a person do.</param>
/// <param name="RequiresMfa">Using it needs a step-up.</param>
/// <param name="Requires">The permissions it is useless without (auth.PermissionDependencies).</param>
public sealed record CataloguePermission(string Code, string Action, string Name, bool RequiresMfa, IReadOnlyList<string> Requires);

/// <summary>A screen and its permissions.</summary>
public sealed record CatalogueScreen(string Code, string Name, IReadOnlyList<CataloguePermission> Permissions);

/// <summary>A module the institution has switched on, with its screens.</summary>
public sealed record CatalogueModule(string Code, string Name, IReadOnlyList<CatalogueScreen> Screens);

/// <summary>A role a member holds.</summary>
public sealed record MemberRole(string Code, string Name, byte Rank);

/// <summary>A person holding at least one role in the institution.</summary>
/// <param name="UserId">The account, as the assign and revoke endpoints take it.</param>
/// <param name="FullName">Their name.</param>
/// <param name="Phone">Masked by the database (dynamic data masking) unless the login may unmask.</param>
/// <param name="Roles">Most senior first.</param>
public sealed record StaffMemberView(Guid UserId, string FullName, string? Phone, IReadOnlyList<MemberRole> Roles);

/// <summary>An open invitation, as the institution sees it.</summary>
public sealed record PendingInvitationView(
    Guid Id,
    string Name,
    string? Phone,
    string RoleCode,
    string RoleName,
    DateTime InvitedAt,
    DateTime ExpiresAt,
    string InvitedBy);

/// <summary>An open invitation, as the person invited sees it.</summary>
public sealed record MyInvitationView(
    Guid Id,
    string Institution,
    string Slug,
    string RoleName,
    DateTime InvitedAt,
    DateTime ExpiresAt,
    string InvitedBy);

/// <summary>Dapper reads for the access screens (plan 04, X6), always of the caller's own institution.</summary>
public interface IAccessQueries
{
    /// <summary>The institution's active roles, most senior first, each with whether the caller may manage it.</summary>
    Task<IReadOnlyList<InstitutionRoleView>> GetRolesAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The institution's switched-on modules, their screens and institution-scope permissions.</summary>
    Task<IReadOnlyList<CatalogueModule>> GetCatalogueAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>Everyone holding a role in the institution now.</summary>
    Task<IReadOnlyList<StaffMemberView>> GetMembersAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The institution's invitations still waiting for an answer.</summary>
    Task<IReadOnlyList<PendingInvitationView>> GetPendingInvitationsAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own invitations still waiting for their answer.</summary>
    Task<IReadOnlyList<MyInvitationView>> GetMyInvitationsAsync(Guid userId, CancellationToken cancellationToken = default);
}

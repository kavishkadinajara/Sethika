namespace Sethika.Application.Abstractions;

/// <summary>An institution as the platform's list shows it (plan 04, Y5).</summary>
/// <param name="Slug">Its public handle, which support mode switches by.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type's name.</param>
/// <param name="District">Its district's name, if it has one.</param>
/// <param name="Status">Onboarding, Active, Suspended or Deactivated.</param>
/// <param name="Owner">The longest-standing owner in force (an assignment of an owner-level role), if any.</param>
/// <param name="Staff">People holding a role there now.</param>
public sealed record AdminInstitutionView(string Slug, string Name, string Type, string? District, string Status, string? Owner, int Staff);

/// <summary>An institution an account holds roles in, with those roles' names.</summary>
public sealed record AdminUserInstitution(string Slug, string Name, IReadOnlyList<string> Roles);

/// <summary>An account as the platform's list shows it (plan 04, Y5).</summary>
/// <param name="Id">The account.</param>
/// <param name="FullName">Their name.</param>
/// <param name="Phone">Masked by the database unless the login may unmask it.</param>
/// <param name="UserType">Donor, InstitutionStaff or PlatformStaff.</param>
/// <param name="Status">Active, PendingVerification, Suspended or Deactivated.</param>
/// <param name="Institutions">Where they hold roles now.</param>
/// <param name="PlatformRoles">The codes of the platform roles they hold now.</param>
public sealed record AdminUserView(
    Guid Id,
    string FullName,
    string? Phone,
    string UserType,
    string Status,
    IReadOnlyList<AdminUserInstitution> Institutions,
    IReadOnlyList<string> PlatformRoles);

/// <summary>A platform role, and whether the caller may hand it out (strictly junior to their rank).</summary>
public sealed record PlatformRoleView(string Code, string Name, byte Rank, bool CanManage);

/// <summary>One page of a platform list (plan 05, X4), and how many there are in all.</summary>
public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>What the institutions list is narrowed and ordered by. Null filters do not narrow.</summary>
/// <param name="Search">Contained in the name, slug, type or district.</param>
/// <param name="Status">Onboarding, Active, Suspended or Deactivated.</param>
/// <param name="Type">An institution type's code.</param>
/// <param name="District">A district's code.</param>
/// <param name="Newest">Newest approved first; otherwise by name.</param>
public sealed record AdminInstitutionFilter(string? Search, string? Status, string? Type, string? District, bool Newest, int Page, int PageSize);

/// <summary>What the accounts list is narrowed and ordered by. Null filters do not narrow.</summary>
/// <param name="Search">Contained in the name or phone number.</param>
/// <param name="Kind">Donor, InstitutionStaff or PlatformStaff.</param>
/// <param name="Status">Active, PendingVerification, Suspended or Deactivated.</param>
/// <param name="ByName">By name; otherwise newest first.</param>
public sealed record AdminUserFilter(string? Search, string? Kind, string? Status, bool ByName, int Page, int PageSize);

/// <summary>How many institutions are in one status: the platform dashboard's panel (plan 05, R4).</summary>
public sealed record InstitutionStatusCount(string Status, int Count);

/// <summary>Dapper reads for the platform's own screens (plan 04, Y5). The caller is platform staff, so RLS shows every tenant.</summary>
public interface IAdminQueries
{
    /// <summary>Every institution status, in lifecycle order, with how many are in it: zero included.</summary>
    Task<IReadOnlyList<InstitutionStatusCount>> GetInstitutionStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of the institutions <paramref name="filter"/> picks, in its order.</summary>
    Task<AdminPage<AdminInstitutionView>> GetInstitutionsAsync(AdminInstitutionFilter filter, CancellationToken cancellationToken = default);

    /// <summary>One page of the accounts <paramref name="filter"/> picks, in its order.</summary>
    Task<AdminPage<AdminUserView>> GetUsersAsync(AdminUserFilter filter, CancellationToken cancellationToken = default);

    /// <summary>The platform roles, each with whether <paramref name="userId"/> may hand it out.</summary>
    Task<IReadOnlyList<PlatformRoleView>> GetPlatformRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}

namespace Sethika.Application.Abstractions;

/// <summary>An institution as the platform's list shows it (plan 04, Y5).</summary>
/// <param name="Slug">Its public handle, which support mode switches by.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type's name.</param>
/// <param name="District">Its district's name, if it has one.</param>
/// <param name="Status">Onboarding, Active, Suspended or Deactivated.</param>
/// <param name="Owner">The active owner's name, if any.</param>
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

/// <summary>Dapper reads for the platform's own screens (plan 04, Y5). The caller is platform staff, so RLS shows every tenant.</summary>
public interface IAdminQueries
{
    /// <summary>Institutions whose name, type or district contains <paramref name="search"/>; every one when it is empty. At most 200.</summary>
    Task<IReadOnlyList<AdminInstitutionView>> GetInstitutionsAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>Accounts whose name or phone contains <paramref name="search"/>; the newest when it is empty. At most 100.</summary>
    Task<IReadOnlyList<AdminUserView>> GetUsersAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>The platform roles, each with whether <paramref name="userId"/> may hand it out.</summary>
    Task<IReadOnlyList<PlatformRoleView>> GetPlatformRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}

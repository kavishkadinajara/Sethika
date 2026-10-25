using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the access screens (plan 04, X6), per the reads-use-Dapper rule.</summary>
/// <remarks>
/// Each is filtered by the institution in the caller's token as well as by RLS where the table has
/// it; <c>auth.*</c> has no RLS, so the <c>InstitutionId</c> filter there is the guarantee.
/// </remarks>
public sealed class AccessQueries(IDbConnectionFactory connections) : IAccessQueries
{
    /// <summary>A role assignment in force now: active, its role active, inside its validity window.</summary>
    private const string InForce =
        """
        ur.IsActive = 1
        AND (ur.ValidFrom IS NULL OR ur.ValidFrom <= SYSUTCDATETIME())
        AND (ur.ValidTo   IS NULL OR ur.ValidTo   >  SYSUTCDATETIME())
        """;

    private const string RolesSql =
        $"""
        WITH ActorRank AS (
            SELECT MIN(x.Rank) AS Rank
            FROM (
            SELECT l.Rank
            FROM auth.UserRoles ur
            JOIN auth.Roles r      ON r.Id = ur.RoleId AND r.IsActive = 1
            JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
            WHERE ur.UserId = @userId AND ur.InstitutionId = @institutionId AND {InForce}
            UNION ALL
            -- Plan 04, Y3: platform support outranks every institution level.
            SELECT CAST(0 AS TINYINT)
            FROM auth.vw_EffectivePermissions ep
            JOIN auth.Users u ON u.Id = ep.UserId AND u.UserType = 'PlatformStaff' AND u.IsDeleted = 0
            WHERE ep.UserId = @userId AND ep.InstitutionId IS NULL AND ep.PermissionCode = 'PLATFORM.INSTITUTIONS.EXECUTE'
            ) x
        ),
        Holders AS (
            SELECT ur.RoleId, COUNT(*) AS Members
            FROM auth.UserRoles ur
            WHERE ur.InstitutionId = @institutionId AND {InForce}
            GROUP BY ur.RoleId
        )
        SELECT r.Code AS Code,
               r.Name AS Name,
               l.Code AS Level,
               l.Name AS LevelName,
               l.Rank AS Rank,
               CAST(CASE WHEN r.TemplateRoleId IS NOT NULL OR r.IsSystem = 1 THEN 1 ELSE 0 END AS BIT) AS IsSystem,
               CAST(CASE WHEN a.Rank IS NOT NULL AND l.Rank > a.Rank THEN 1 ELSE 0 END AS BIT) AS CanManage,
               ISNULL(h.Members, 0) AS Members,
               p.Code AS Permission
        FROM auth.Roles r
        JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
        CROSS JOIN ActorRank a
        LEFT JOIN Holders h               ON h.RoleId = r.Id
        LEFT JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
        LEFT JOIN auth.Permissions p      ON p.Id = rp.PermissionId AND p.IsActive = 1
        WHERE r.InstitutionId = @institutionId AND r.IsActive = 1
        ORDER BY l.Rank, r.Name, r.Code, p.Code;
        """;

    private const string CatalogueSql =
        """
        SELECT m.Code AS ModuleCode,
               m.Name AS ModuleName,
               s.Code AS ScreenCode,
               s.Name AS ScreenName,
               p.Code AS Code,
               a.Code AS Action,
               p.Name AS Name,
               p.RequiresMfa AS RequiresMfa
        FROM auth.Modules m
        JOIN auth.Portals po            ON po.Id = m.PortalId AND po.Code = 'INSTITUTION'
        JOIN org.InstitutionModules im  ON im.ModuleId = m.Id AND im.InstitutionId = @institutionId AND im.IsEnabled = 1
        JOIN auth.Screens s             ON s.ModuleId = m.Id AND s.IsActive = 1
        JOIN auth.Permissions p         ON p.ScreenId = s.Id AND p.IsActive = 1 AND p.Scope = 'Institution'
        JOIN auth.PermissionActions a   ON a.Id = p.ActionId
        WHERE m.IsActive = 1
        ORDER BY m.SortOrder, m.Code, s.SortOrder, s.Code, a.SortOrder, a.Id;

        SELECT p.Code AS Code, r.Code AS Requires
        FROM auth.PermissionDependencies d
        JOIN auth.Permissions p ON p.Id = d.PermissionId
        JOIN auth.Permissions r ON r.Id = d.RequiresPermissionId
        ORDER BY p.Code, r.Code;
        """;

    private const string MembersSql =
        $"""
        SELECT u.Id AS UserId,
               u.FullName AS FullName,
               u.PhoneNumber AS Phone,
               r.Code AS RoleCode,
               r.Name AS RoleName,
               l.Rank AS Rank
        FROM auth.UserRoles ur
        JOIN auth.Users u      ON u.Id = ur.UserId AND u.IsDeleted = 0
        JOIN auth.Roles r      ON r.Id = ur.RoleId AND r.IsActive = 1
        JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
        WHERE ur.InstitutionId = @institutionId AND {InForce}
        ORDER BY u.FullName, u.Id, l.Rank, r.Name;
        """;

    private const string PendingSql =
        """
        SELECT i.PublicId AS Id,
               i.InviteeName AS Name,
               i.InviteePhone AS Phone,
               r.Code AS RoleCode,
               r.Name AS RoleName,
               i.InvitedAt AS InvitedAt,
               i.ExpiresAt AS ExpiresAt,
               u.FullName AS InvitedBy
        FROM org.StaffInvitations i
        JOIN auth.Roles r ON r.Id = i.RoleId
        JOIN auth.Users u ON u.Id = i.InvitedBy
        WHERE i.InstitutionId = @institutionId AND i.Status = 'Pending' AND i.ExpiresAt > SYSUTCDATETIME()
        ORDER BY i.InvitedAt DESC;
        """;

    private const string MineSql =
        """
        SELECT i.PublicId AS Id,
               ins.Name AS Institution,
               ins.Slug AS Slug,
               r.Name AS RoleName,
               i.InvitedAt AS InvitedAt,
               i.ExpiresAt AS ExpiresAt,
               u.FullName AS InvitedBy
        FROM org.StaffInvitations i
        JOIN org.Institutions ins ON ins.Id = i.InstitutionId AND ins.Status = 'Active'
        JOIN auth.Roles r         ON r.Id = i.RoleId
        JOIN auth.Users u         ON u.Id = i.InvitedBy
        WHERE i.InviteeUserId = @userId AND i.Status = 'Pending' AND i.ExpiresAt > SYSUTCDATETIME()
        ORDER BY i.InvitedAt DESC;
        """;

    public async Task<IReadOnlyList<InstitutionRoleView>> GetRolesAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<RoleRow>(
            new CommandDefinition(RolesSql, new { userId, institutionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows
            .GroupBy(r => (r.Code, r.Name, r.Level, r.LevelName, r.Rank, r.IsSystem, r.CanManage, r.Members))
            .Select(g => new InstitutionRoleView(
                g.Key.Code,
                g.Key.Name,
                g.Key.Level,
                g.Key.LevelName,
                g.Key.Rank,
                g.Key.IsSystem,
                g.Key.CanManage,
                g.Key.Members,
                [.. g.Where(r => r.Permission is not null).Select(r => r.Permission!)]))];
    }

    public async Task<IReadOnlyList<CatalogueModule>> GetCatalogueAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(CatalogueSql, new { institutionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var rows = (await results.ReadAsync<CatalogueRow>().ConfigureAwait(false)).ToList();
        var requires = (await results.ReadAsync<DependencyRow>().ConfigureAwait(false))
            .ToLookup(d => d.Code, d => d.Requires, StringComparer.Ordinal);

        // GroupBy keeps the SQL's order: modules, then screens, then actions, as sorted there.
        return [.. rows
            .GroupBy(r => (r.ModuleCode, r.ModuleName))
            .Select(module => new CatalogueModule(
                module.Key.ModuleCode,
                module.Key.ModuleName,
                [.. module
                    .GroupBy(r => (r.ScreenCode, r.ScreenName))
                    .Select(screen => new CatalogueScreen(
                        screen.Key.ScreenCode,
                        screen.Key.ScreenName,
                        [.. screen.Select(p => new CataloguePermission(p.Code, p.Action, p.Name, p.RequiresMfa, [.. requires[p.Code]]))]))]))];
    }

    public async Task<IReadOnlyList<StaffMemberView>> GetMembersAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MemberRow>(
            new CommandDefinition(MembersSql, new { institutionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows
            .GroupBy(r => (r.UserId, r.FullName, r.Phone))
            .Select(g => new StaffMemberView(
                g.Key.UserId,
                g.Key.FullName,
                g.Key.Phone,
                [.. g.OrderBy(r => r.Rank).ThenBy(r => r.RoleName, StringComparer.Ordinal).Select(r => new MemberRole(r.RoleCode, r.RoleName, r.Rank))]))];
    }

    public async Task<IReadOnlyList<PendingInvitationView>> GetPendingInvitationsAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PendingInvitationView>(
            new CommandDefinition(PendingSql, new { institutionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<IReadOnlyList<MyInvitationView>> GetMyInvitationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MyInvitationView>(
            new CommandDefinition(MineSql, new { userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows];
    }

    private sealed record RoleRow(
        string Code,
        string Name,
        string Level,
        string LevelName,
        byte Rank,
        bool IsSystem,
        bool CanManage,
        int Members,
        string? Permission);

    private sealed record CatalogueRow(
        string ModuleCode,
        string ModuleName,
        string ScreenCode,
        string ScreenName,
        string Code,
        string Action,
        string Name,
        bool RequiresMfa);

    private sealed record DependencyRow(string Code, string Requires);

    private sealed record MemberRow(Guid UserId, string FullName, string? Phone, string RoleCode, string RoleName, byte Rank);
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the platform's own screens (plan 04, Y5), per the reads-use-Dapper rule.</summary>
/// <remarks>
/// Only platform staff reach these endpoints, and their connections carry <c>IsPlatform = 1</c>, so
/// RLS shows every tenant. Each list is one page and a count, read in one batch (as the review
/// queue is), so a page past the end still knows the total.
/// </remarks>
public sealed class AdminQueries(IDbConnectionFactory connections) : IAdminQueries
{
    /// <summary>A role assignment in force now: active, inside its validity window.</summary>
    private const string InForce =
        """
        ur.IsActive = 1
        AND (ur.ValidFrom IS NULL OR ur.ValidFrom <= SYSUTCDATETIME())
        AND (ur.ValidTo   IS NULL OR ur.ValidTo   >  SYSUTCDATETIME())
        """;

    /// <summary>The institutions a filter picks; shared by the page and its count.</summary>
    private const string InstitutionsFrom =
        """
        FROM org.Institutions i
        JOIN org.InstitutionTypes t ON t.Id = i.InstitutionTypeId
        LEFT JOIN core.Districts d  ON d.Id = i.DistrictId
        WHERE i.IsDeleted = 0
          AND (@like IS NULL OR i.Name LIKE @like OR t.Name LIKE @like OR d.Name LIKE @like OR i.Slug LIKE @like)
          AND (@status IS NULL OR i.Status = @status)
          AND (@type IS NULL OR t.Code = @type)
          AND (@district IS NULL OR d.Code = @district)
        """;

    // The owner is whoever holds an owner-level role there now, the rule the last-owner guard uses
    // (RoleManagementHandler); org.InstitutionMembers.IsOwner is not kept in step with the roles.
    private const string InstitutionsSql =
        $"""
        SELECT i.Slug AS Slug,
               i.Name AS Name,
               t.Name AS Type,
               d.Name AS District,
               i.Status AS Status,
               (SELECT TOP (1) u.FullName
                FROM auth.UserRoles ur
                JOIN auth.Roles r      ON r.Id = ur.RoleId AND r.IsActive = 1
                JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId AND l.Scope = 'Institution' AND l.Rank = 1
                JOIN auth.Users u      ON u.Id = ur.UserId
                WHERE ur.InstitutionId = i.Id AND {InForce}
                ORDER BY ur.Id) AS Owner,
               (SELECT COUNT(DISTINCT ur.UserId)
                FROM auth.UserRoles ur
                JOIN auth.Roles r ON r.Id = ur.RoleId AND r.IsActive = 1
                WHERE ur.InstitutionId = i.Id AND {InForce}) AS Staff
        {InstitutionsFrom}
        ORDER BY CASE WHEN @newest = 1 THEN i.ApprovedAt END DESC, i.Name, i.Slug
        OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

        SELECT COUNT(*) {InstitutionsFrom};
        """;

    // Every status from the table's CHECK, so one nobody is in still shows as zero.
    private const string StatusesSql =
        """
        SELECT s.Status AS Status, COUNT(i.Id) AS Count
        FROM (VALUES ('Onboarding', 1), ('Active', 2), ('Suspended', 3), ('Deactivated', 4)) AS s (Status, SortOrder)
        LEFT JOIN org.Institutions AS i ON i.Status = s.Status AND i.IsDeleted = 0
        GROUP BY s.Status, s.SortOrder
        ORDER BY s.SortOrder;
        """;

    /// <summary>The accounts a filter picks; shared by the page and its count.</summary>
    /// <remarks>
    /// The kind is read from the roles, as the owner is: platform staff by the stored type (the one
    /// the permission view trusts), then anyone holding an institution's role now, then a donor. The
    /// stored type is set only when a platform role goes, so approval and invitations leave staff as
    /// donors there.
    /// </remarks>
    private const string UsersFrom =
        $"""
        FROM auth.Users u
        CROSS APPLY (SELECT CASE
            WHEN u.UserType = 'PlatformStaff' THEN 'PlatformStaff'
            WHEN EXISTS (SELECT 1
                         FROM auth.UserRoles ur
                         JOIN auth.Roles r ON r.Id = ur.RoleId AND r.IsActive = 1
                         WHERE ur.UserId = u.Id AND ur.InstitutionId IS NOT NULL AND {InForce}) THEN 'InstitutionStaff'
            ELSE 'Donor' END AS Kind) k
        WHERE u.IsDeleted = 0
          AND (@like IS NULL OR u.FullName LIKE @like OR u.PhoneNumber LIKE @like)
          AND (@kind IS NULL OR k.Kind = @kind)
          AND (@status IS NULL OR u.Status = @status)
          AND (@institution IS NULL OR EXISTS (SELECT 1
                         FROM auth.UserRoles ur
                         JOIN auth.Roles r       ON r.Id = ur.RoleId AND r.IsActive = 1
                         JOIN org.Institutions i ON i.Id = ur.InstitutionId
                         WHERE ur.UserId = u.Id AND i.Slug = @institution AND {InForce}))
        """;

    private const string UsersSql =
        $"""
        SELECT u.Id AS Id,
               u.FullName AS FullName,
               u.PhoneNumber AS Phone,
               k.Kind AS UserType,
               u.Status AS Status
        {UsersFrom}
        ORDER BY CASE WHEN @byName = 1 THEN u.FullName END, u.CreatedAt DESC, u.Id
        OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

        SELECT COUNT(*) {UsersFrom};
        """;

    private const string UserRolesSql =
        $"""
        SELECT ur.UserId AS UserId,
               i.Slug AS Slug,
               i.Name AS Institution,
               r.Code AS RoleCode,
               r.Name AS RoleName
        FROM auth.UserRoles ur
        JOIN auth.Roles r             ON r.Id = ur.RoleId AND r.IsActive = 1
        LEFT JOIN org.Institutions i  ON i.Id = ur.InstitutionId
        WHERE ur.UserId IN @ids AND {InForce}
          AND (ur.InstitutionId IS NOT NULL OR r.Scope = 'Platform')
        ORDER BY i.Name, r.Name;
        """;

    private const string PlatformRolesSql =
        $"""
        WITH ActorRank AS (
            SELECT MIN(l.Rank) AS Rank
            FROM auth.UserRoles ur
            JOIN auth.Roles r      ON r.Id = ur.RoleId AND r.IsActive = 1 AND r.Scope = 'Platform' AND r.InstitutionId IS NULL
            JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
            WHERE ur.UserId = @userId AND ur.InstitutionId IS NULL AND {InForce}
        )
        SELECT r.Code AS Code,
               r.Name AS Name,
               l.Rank AS Rank,
               CAST(CASE WHEN a.Rank IS NOT NULL AND l.Rank > a.Rank THEN 1 ELSE 0 END AS BIT) AS CanManage
        FROM auth.Roles r
        JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
        CROSS JOIN ActorRank a
        WHERE r.InstitutionId IS NULL AND r.Scope = 'Platform' AND r.IsActive = 1
        ORDER BY l.Rank, r.Name;
        """;

    public async Task<AdminPage<AdminInstitutionView>> GetInstitutionsAsync(AdminInstitutionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            InstitutionsSql,
            new
            {
                like = Like(filter.Search),
                status = filter.Status,
                type = filter.Type,
                district = filter.District,
                newest = filter.Newest,
                skip = (filter.Page - 1) * filter.PageSize,
                take = filter.PageSize,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var items = (await grid.ReadAsync<AdminInstitutionView>().ConfigureAwait(false)).AsList();
        var total = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return new AdminPage<AdminInstitutionView>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<InstitutionStatusCount>> GetInstitutionStatusesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<InstitutionStatusCount>(
            new CommandDefinition(StatusesSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<AdminPage<AdminUserView>> GetUsersAsync(AdminUserFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<UserRow> users;
        int total;
        await using (var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            UsersSql,
            new
            {
                like = Like(filter.Search),
                kind = filter.Kind,
                status = filter.Status,
                institution = filter.Institution,
                byName = filter.ByName,
                skip = (filter.Page - 1) * filter.PageSize,
                take = filter.PageSize,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false))
        {
            users = (await grid.ReadAsync<UserRow>().ConfigureAwait(false)).AsList();
            total = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        }

        if (users.Count == 0)
        {
            return new AdminPage<AdminUserView>([], total, filter.Page, filter.PageSize);
        }

        var roles = (await connection.QueryAsync<UserRoleRow>(
                new CommandDefinition(UserRolesSql, new { ids = users.Select(u => u.Id).ToArray() }, cancellationToken: cancellationToken))
            .ConfigureAwait(false))
            .ToLookup(r => r.UserId);

        return new AdminPage<AdminUserView>([.. users.Select(u => new AdminUserView(
            u.Id,
            u.FullName,
            u.Phone,
            u.UserType,
            u.Status,
            [.. roles[u.Id]
                .Where(r => r.Slug is not null)
                .GroupBy(r => (r.Slug!, r.Institution!))
                .Select(g => new AdminUserInstitution(g.Key.Item1, g.Key.Item2, [.. g.Select(r => r.RoleName)]))],
            [.. roles[u.Id].Where(r => r.Slug is null).Select(r => r.RoleCode)]))], total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<PlatformRoleView>> GetPlatformRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PlatformRoleView>(
            new CommandDefinition(PlatformRolesSql, new { userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows];
    }

    /// <summary>A LIKE pattern for "contains", with the search's own wildcards taken literally; null for no search.</summary>
    private static string? Like(string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return null;
        }

        var escaped = term.Replace("[", "[[]", StringComparison.Ordinal)
                          .Replace("%", "[%]", StringComparison.Ordinal)
                          .Replace("_", "[_]", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private sealed record UserRow(Guid Id, string FullName, string? Phone, string UserType, string Status);

    private sealed record UserRoleRow(Guid UserId, string? Slug, string? Institution, string RoleCode, string RoleName);
}

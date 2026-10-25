using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the platform's own screens (plan 04, Y5), per the reads-use-Dapper rule.</summary>
/// <remarks>
/// Only platform staff reach these endpoints, and their connections carry <c>IsPlatform = 1</c>, so
/// RLS shows every tenant: the owner's name from <c>org.InstitutionMembers</c> is visible here.
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

    private const string InstitutionsSql =
        $"""
        SELECT TOP (200)
               i.Slug AS Slug,
               i.Name AS Name,
               t.Name AS Type,
               d.Name AS District,
               i.Status AS Status,
               (SELECT TOP (1) u.FullName
                FROM org.InstitutionMembers m
                JOIN auth.Users u ON u.Id = m.UserId
                WHERE m.InstitutionId = i.Id AND m.IsOwner = 1 AND m.Status = 'Active') AS Owner,
               (SELECT COUNT(DISTINCT ur.UserId)
                FROM auth.UserRoles ur
                JOIN auth.Roles r ON r.Id = ur.RoleId AND r.IsActive = 1
                WHERE ur.InstitutionId = i.Id AND {InForce}) AS Staff
        FROM org.Institutions i
        JOIN org.InstitutionTypes t ON t.Id = i.InstitutionTypeId
        LEFT JOIN core.Districts d  ON d.Id = i.DistrictId
        WHERE i.IsDeleted = 0
          AND (@like IS NULL OR i.Name LIKE @like OR t.Name LIKE @like OR d.Name LIKE @like OR i.Slug LIKE @like)
        ORDER BY i.Name, i.Slug;
        """;

    private const string UsersSql =
        """
        SELECT TOP (100)
               u.Id AS Id,
               u.FullName AS FullName,
               u.PhoneNumber AS Phone,
               u.UserType AS UserType,
               u.Status AS Status
        FROM auth.Users u
        WHERE u.IsDeleted = 0
          AND (@like IS NULL OR u.FullName LIKE @like OR u.PhoneNumber LIKE @like)
        ORDER BY u.CreatedAt DESC, u.Id;
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

    public async Task<IReadOnlyList<AdminInstitutionView>> GetInstitutionsAsync(string? search, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<AdminInstitutionView>(
            new CommandDefinition(InstitutionsSql, new { like = Like(search) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<IReadOnlyList<AdminUserView>> GetUsersAsync(string? search, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var users = (await connection.QueryAsync<UserRow>(
            new CommandDefinition(UsersSql, new { like = Like(search) }, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        if (users.Count == 0)
        {
            return [];
        }

        var roles = (await connection.QueryAsync<UserRoleRow>(
                new CommandDefinition(UserRolesSql, new { ids = users.Select(u => u.Id).ToArray() }, cancellationToken: cancellationToken))
            .ConfigureAwait(false))
            .ToLookup(r => r.UserId);

        return [.. users.Select(u => new AdminUserView(
            u.Id,
            u.FullName,
            u.Phone,
            u.UserType,
            u.Status,
            [.. roles[u.Id]
                .Where(r => r.Slug is not null)
                .GroupBy(r => (r.Slug!, r.Institution!))
                .Select(g => new AdminUserInstitution(g.Key.Item1, g.Key.Item2, [.. g.Select(r => r.RoleName)]))],
            [.. roles[u.Id].Where(r => r.Slug is null).Select(r => r.RoleCode)]))];
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

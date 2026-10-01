using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper read of what an actor may grant, per the reads-use-Dapper rule.</summary>
/// <remarks>
/// The same three rules the role commands enforce, as a read: rank strictly more junior, inside the
/// ceiling, held by the actor. It is what the role editor offers; the commands check again inside
/// their transactions, so a stale screen cannot grant more than this says.
/// </remarks>
public sealed class RoleLevelQueries(IDbConnectionFactory connections) : IRoleLevelQueries
{
    private const string GrantableSql =
        """
        WITH ActorRank AS (
            SELECT MIN(l.Rank) AS Rank
            FROM auth.UserRoles ur
            JOIN auth.Roles r      ON r.Id = ur.RoleId AND r.IsActive = 1
            JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
            WHERE ur.UserId = @userId
              AND ur.InstitutionId = @institutionId
              AND ur.IsActive = 1
              AND (ur.ValidFrom IS NULL OR ur.ValidFrom <= SYSUTCDATETIME())
              AND (ur.ValidTo   IS NULL OR ur.ValidTo   >  SYSUTCDATETIME())
        ),
        Grantable AS (
            SELECT c.RoleLevelId, p.Code
            FROM auth.RoleLevelPermissions c
            JOIN auth.Permissions p ON p.Id = c.PermissionId AND p.IsActive = 1
            WHERE EXISTS (SELECT 1 FROM auth.vw_EffectivePermissions ep
                          WHERE ep.UserId = @userId AND ep.InstitutionId = @institutionId
                            AND ep.PermissionId = c.PermissionId)
        )
        SELECT l.Code AS Level,
               l.Name AS Name,
               l.Rank AS Rank,
               g.Code AS Permission
        FROM auth.RoleLevels l
        JOIN ActorRank a      ON l.Rank > a.Rank
        LEFT JOIN Grantable g ON g.RoleLevelId = l.Id
        WHERE l.Scope = 'Institution'
          AND l.IsActive = 1
        ORDER BY l.Rank, g.Code;
        """;

    public async Task<IReadOnlyList<GrantableLevel>> GetGrantableAsync(
        Guid userId,
        int institutionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            GrantableSql,
            new { userId, institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows
            .GroupBy(r => (r.Level, r.Name, r.Rank))
            .Select(g => new GrantableLevel(
                g.Key.Level,
                g.Key.Name,
                g.Key.Rank,
                [.. g.Where(r => r.Permission is not null).Select(r => r.Permission!)]))];
    }

    private sealed record Row(string Level, string Name, byte Rank, string? Permission);
}

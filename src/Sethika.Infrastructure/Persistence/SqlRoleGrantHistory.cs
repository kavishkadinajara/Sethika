using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IRoleGrantHistory"/> through EF Core's temporal query (<c>FOR SYSTEM_TIME ALL</c>): the
/// current rows and every history row, on the context's own connection.
/// </summary>
public sealed class SqlRoleGrantHistory(SethikaDbContext context) : IRoleGrantHistory
{
    public async Task<IReadOnlyCollection<(int RoleId, int PermissionId)>> EverHeldAsync(IReadOnlyList<int> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        if (roleIds.Count == 0)
        {
            return [];
        }

        var rows = await context.RolePermissions
            .TemporalAll()
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(r => (r.RoleId, r.PermissionId))];
    }
}

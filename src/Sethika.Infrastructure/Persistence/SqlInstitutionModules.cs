using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IInstitutionModules"/> as one INSERT ... SELECT on the context's own transaction.
/// </summary>
/// <remarks>
/// Set-based because every source is a table: the module catalogue, the type's defaults and the
/// plan's modules. A module in more than one is enabled once, as Default if any default source
/// names it. Modules of other portals (the donor and admin portals) are never an institution's.
/// <c>org.InstitutionModules</c> has no Row-Level Security, so a donor's request reads it too.
/// </remarks>
public sealed class SqlInstitutionModules(SethikaDbContext context) : IInstitutionModules
{
    public async Task<bool> IsOnAsync(int institutionId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var code = new SqlParameter("code", SqlDbType.VarChar, 50) { Value = moduleCode };

        var on = await context.Database.SqlQuery<int>(
            $"""
            SELECT COUNT(*) AS Value
            FROM org.InstitutionModules AS im
            JOIN auth.Modules AS m ON m.Id = im.ModuleId
            WHERE im.InstitutionId = {institutionId} AND im.IsEnabled = 1 AND m.Code = {code} AND m.IsActive = 1
            """).SingleAsync(cancellationToken).ConfigureAwait(false);

        return on > 0;
    }

    public Task<int> EnableForNewInstitutionAsync(
        int institutionId,
        short institutionTypeId,
        short? planId,
        Guid enabledBy,
        string reason,
        CancellationToken cancellationToken = default) =>
        context.Database.ExecuteSqlAsync(
            $"""
            DECLARE @planId SMALLINT = COALESCE({planId}, (SELECT Id FROM org.Plans WHERE Code = 'FREE'));

            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy, ChangeReason)
            SELECT {institutionId}, m.Id, 1,
                   CASE WHEN m.IsCore = 1 OR d.ModuleId IS NOT NULL THEN 'Default' ELSE 'Plan' END,
                   {enabledBy}, {reason}
            FROM auth.Modules AS m
            JOIN auth.Portals AS p ON p.Id = m.PortalId AND p.Code = 'INSTITUTION'
            LEFT JOIN org.InstitutionTypeDefaultModules AS d ON d.ModuleId = m.Id AND d.InstitutionTypeId = {institutionTypeId}
            LEFT JOIN org.PlanModules AS pm ON pm.ModuleId = m.Id AND pm.PlanId = @planId
            WHERE m.IsActive = 1
              AND (m.IsCore = 1 OR d.ModuleId IS NOT NULL OR pm.ModuleId IS NOT NULL);
            """,
            cancellationToken);
}

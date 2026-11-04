using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.Platform;

/// <summary>What bringing an institution up to its templates added.</summary>
/// <param name="Found">Whether the institution exists; nothing else is set when it does not.</param>
public sealed record TemplateSyncResult(bool Found, int Modules, int Roles, int Grants)
{
    internal static TemplateSyncResult NotFound { get; } = new(false, 0, 0, 0);
}

/// <summary>
/// The platform action "bring this institution's roles up to their templates" (plan 09, Q4).
/// </summary>
/// <remarks>
/// <para>
/// An institution's roles are copied from the templates when it is approved, so one approved before a
/// screen was built never holds that screen's permissions: <c>BOOKINGS.SPONSORS</c> (plan 08, K17), the
/// services (Phase E) and giving (Phase D). This adds what is missing and never takes anything away:
/// </para>
/// <list type="number">
/// <item>the core modules and the type's default modules the institution has no row for (a module with
/// a row, on or off, is the institution's own state);</item>
/// <item>a template role the institution has no copy of, with the template's grants;</item>
/// <item>for each copied role, each template grant it does not hold and never held. A grant in the
/// role's history was taken away by someone on purpose, so it stays away; the temporal history table is
/// what tells the two apart.</item>
/// </list>
/// <para>
/// One transaction; the institution's <c>PermissionsVersion</c> is bumped so no cached permission set
/// outlives it, and an audit row names the counts. Running it again adds nothing.
/// </para>
/// </remarks>
public sealed class TemplateSyncHandler(ISethikaDbContext db, IInstitutionModules modules, IRoleGrantHistory history, TimeProvider timeProvider)
{
    private const string InstitutionScope = "Institution";

    public async Task<TemplateSyncResult> SyncAsync(string slug, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var institution = await db.Institutions
            .AsNoTracking()
            .Where(i => i.Slug == slug && !i.IsDeleted)
            .Select(i => new { i.Id, i.InstitutionTypeId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (institution is null)
        {
            return TemplateSyncResult.NotFound;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // 1. Modules the catalogue gained.
        var modulesAdded = await modules
            .EnableMissingDefaultsAsync(institution.Id, institution.InstitutionTypeId, actorId, "Brought up to the templates", cancellationToken)
            .ConfigureAwait(false);

        // 2. Template roles it has no copy of.
        var templates = await db.Roles
            .AsNoTracking()
            .Where(r => r.IsTemplate && r.InstitutionId == null && r.IsActive
                        && (r.InstitutionTypeId == null || r.InstitutionTypeId == institution.InstitutionTypeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roles = await db.Roles
            .Where(r => r.InstitutionId == institution.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = new List<Role>();
        foreach (var template in templates.Where(t => !roles.Any(r => r.TemplateRoleId == t.Id)))
        {
            // A role of the institution's own already holds the code (UX_Roles_Code_Inst): leave it.
            if (roles.Any(r => string.Equals(r.Code, template.Code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            added.Add(new Role
            {
                InstitutionId = institution.Id,
                Scope = InstitutionScope,
                RoleLevelId = template.RoleLevelId,
                Code = template.Code,
                Name = template.Name,
                Description = template.Description,
                TemplateRoleId = template.Id,
                IsActive = true,
                CreatedBy = actorId,
            });
        }

        db.Roles.AddRange(added);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 3. Grants each copy's template holds and the copy never did.
        var copies = roles.Where(r => r.TemplateRoleId is not null && r.IsActive).Concat(added).ToList();
        var templateIds = copies.Select(r => r.TemplateRoleId!.Value).Distinct().ToList();
        var copyIds = copies.Select(r => r.Id).ToList();

        var templateGrants = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => templateIds.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var held = (await history.EverHeldAsync(copyIds, cancellationToken).ConfigureAwait(false)).ToHashSet();
        var grants = 0;
        foreach (var copy in copies)
        {
            foreach (var grant in templateGrants.Where(g => g.RoleId == copy.TemplateRoleId))
            {
                if (held.Add((copy.Id, grant.PermissionId)))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = copy.Id, PermissionId = grant.PermissionId, GrantedBy = actorId });
                    grants++;
                }
            }
        }

        if (modulesAdded > 0 || added.Count > 0 || grants > 0)
        {
            await db.Institutions
                .Where(i => i.Id == institution.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PermissionsVersion, i => i.PermissionsVersion + 1), cancellationToken)
                .ConfigureAwait(false);
        }

        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = actorId,
            InstitutionId = institution.Id,
            Action = "SyncTemplates",
            EntityName = "Institution",
            EntityId = slug,
            NewValues = JsonSerializer.Serialize(new { modules = modulesAdded, roles = added.Count, grants }),
            OccurredAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TemplateSyncResult(true, modulesAdded, added.Count, grants);
    }
}

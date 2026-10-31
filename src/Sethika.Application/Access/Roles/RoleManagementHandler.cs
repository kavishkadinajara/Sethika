using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Platform;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.Roles;

/// <summary>
/// Institution roles, made and handed out by the institution's own people - plan 5.9, A15.
/// </summary>
/// <remarks>
/// Three rules stop anyone raising themselves or others above what they are trusted with:
/// <list type="number">
/// <item>The role's level must be strictly more junior than the actor's most senior rank here, so
/// a manager manages staff, never managers or owners - and no one reaches an owner role at all,
/// since rank 1 is the most senior there is. Owners are made by onboarding approval.</item>
/// <item>Every permission added must be inside the level's ceiling.</item>
/// <item>Every permission added must be one the actor holds (CLAUDE.md), and so must everything a
/// role grants here before the actor assigns it or invites someone to it.</item>
/// </list>
/// Each is checked inside the change's transaction against rows read in it - the actor's roles,
/// the ceiling, <c>auth.vw_EffectivePermissions</c> - never against the permission cache, which
/// can be up to a version behind. A refusal names the offending permissions and changes nothing;
/// nothing is ever silently dropped from a request.
/// </remarks>
public sealed partial class RoleManagementHandler(ISethikaDbContext db, TimeProvider timeProvider, IPhoneNumberNormalizer phoneNumbers)
{
    private const string InstitutionScope = "Institution";
    private const string ChangedEvent = "RolePermissionsChanged";
    private const int NameMaxLength = 100;

    /// <summary>The most senior institution level (OWNER); an institution always keeps someone at it.</summary>
    private const byte OwnerRank = 1;

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,49}$")]
    private static partial Regex RoleCode();

    /// <summary>Creates a role at a level more junior than the actor's, with permissions the actor may grant.</summary>
    public async Task<RoleChangeResult> CreateAsync(RoleActor actor, CreateRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code?.Trim().ToUpperInvariant();
        if (code is null || !RoleCode().IsMatch(code))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvalidCode);
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvalidName);
        }

        var levelCode = command.Level?.Trim().ToUpperInvariant();
        var level = await db.RoleLevels
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Code == levelCode && l.Scope == InstitutionScope && l.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (level is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.UnknownLevel);
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var refusal = await CheckRankAsync(actor, level.Rank, at, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await db.Roles.AnyAsync(r => r.InstitutionId == actor.InstitutionId && r.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleExists);
        }

        var (permissions, unknown) = await ResolveAsync(command.Permissions, cancellationToken).ConfigureAwait(false);
        if (unknown.Count > 0)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.UnknownPermission, unknown);
        }

        refusal = await CheckGrantableAsync(actor, level.Id, permissions, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        var role = new Role
        {
            InstitutionId = actor.InstitutionId,
            Scope = InstitutionScope,
            RoleLevelId = level.Id,
            Code = code,
            Name = name,
            IsActive = true,
            CreatedBy = actor.UserId,
        };

        db.Roles.Add(role);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // The same code, created by someone else after the check above (UX_Roles_Code_Inst).
            return RoleChangeResult.Failed(RoleChangeFailure.RoleExists);
        }

        foreach (var permission in permissions)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, GrantedBy = actor.UserId });
        }

        // No version bump: nobody holds the role yet, so nobody's permissions changed.
        Record(actor, role.Id, code, "Created", targetUserId: null, at);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RoleChangeResult.Success;
    }

    /// <summary>
    /// Replaces what a role grants. Only the permissions being added are checked against the ceiling
    /// and the actor's own: taking one away is always within an actor who may manage the role.
    /// </summary>
    public async Task<RoleChangeResult> SetPermissionsAsync(
        RoleActor actor,
        string roleCode,
        IReadOnlyCollection<string>? permissionCodes,
        byte[]? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (version is not { Length: 8 })
        {
            return RoleChangeResult.Failed(RoleChangeFailure.InvalidVersion);
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindRoleAsync(actor, roleCode, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleNotFound);
        }

        var refusal = await CheckRankAsync(actor, role.Rank, at, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        var (desired, unknown) = await ResolveAsync(permissionCodes, cancellationToken).ConfigureAwait(false);
        if (unknown.Count > 0)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.UnknownPermission, unknown);
        }

        var current = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var currentIds = current.Select(rp => rp.PermissionId).ToHashSet();
        var desiredIds = desired.Select(p => p.Id).ToHashSet();
        var added = desired.Where(p => !currentIds.Contains(p.Id)).ToList();
        var removed = current.Where(rp => !desiredIds.Contains(rp.PermissionId)).ToList();

        refusal = await CheckGrantableAsync(actor, role.LevelId, added, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        if (added.Count == 0 && removed.Count == 0)
        {
            return RoleChangeResult.Edited(version);
        }

        db.RolePermissions.RemoveRange(removed);
        foreach (var permission in added)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, GrantedBy = actor.UserId });
        }

        // Everyone holding the role is affected, so the institution's version moves: it is in every
        // member's cache key, and the next permission check misses and reloads (CLAUDE.md).
        await db.Institutions
            .Where(i => i.Id == actor.InstitutionId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PermissionsVersion, i => i.PermissionsVersion + 1), cancellationToken)
            .ConfigureAwait(false);

        // The role's own row is updated too, and its WHERE compares the version the editor read: a
        // save made in between by anyone makes this one stale, instead of the two sets merging.
        var tracked = await db.Roles.SingleAsync(r => r.Id == role.Id, cancellationToken).ConfigureAwait(false);
        db.Entry(tracked).Property(r => r.RowVersion).OriginalValue = version;
        tracked.UpdatedBy = actor.UserId;
        db.Entry(tracked).Property(r => r.UpdatedBy).IsModified = true;

        Record(actor, role.Id, role.Code, "PermissionsChanged", targetUserId: null, at);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or DuplicateKeyException)
        {
            // Stale, or a concurrent save of the same permission got its row in first.
            return RoleChangeResult.Failed(RoleChangeFailure.Conflict);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // EF reads a ROWVERSION back after the UPDATE, so this is the new one.
        return RoleChangeResult.Edited(tracked.RowVersion);
    }

    /// <summary>Gives a member of the institution a role more junior than the actor's.</summary>
    /// <remarks>
    /// Only to someone who already holds a role here. Bringing a new person into an institution is
    /// an invitation they accept, not something done to their account.
    /// </remarks>
    public async Task<RoleChangeResult> AssignAsync(
        RoleActor actor,
        string roleCode,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (userId == actor.UserId)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.OwnRoles);
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindRoleAsync(actor, roleCode, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleNotFound);
        }

        var refusal = await CheckRankAsync(actor, role.Rank, at, cancellationToken).ConfigureAwait(false)
                      ?? await CheckRoleHeldAsync(actor, role, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        var memberships = await ActiveAssignments(userId, actor.InstitutionId, at)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (memberships.Count == 0)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.MemberNotFound);
        }

        if (memberships.Contains(role.Id))
        {
            return RoleChangeResult.Success;
        }

        // An active row here can only be one whose validity window lapsed; UX_UserRoles allows one
        // active row per user and role, so it is reopened rather than a second added.
        var lapsed = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (lapsed is null)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = role.Id,
                InstitutionId = actor.InstitutionId,
                AssignedBy = actor.UserId,
                IsActive = true,
            });
        }
        else
        {
            lapsed.ValidFrom = null;
            lapsed.ValidTo = null;
            lapsed.AssignedBy = actor.UserId;
        }

        await BumpUserVersionAsync(userId, cancellationToken).ConfigureAwait(false);
        Record(actor, role.Id, role.Code, "Assigned", userId, at);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // A concurrent assignment of the same role got there first (UX_UserRoles): they hold it,
            // which is what was asked - the same answer as when they already held it.
            return RoleChangeResult.Success;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RoleChangeResult.Success;
    }

    /// <summary>Takes a role more junior than the actor's away from a member.</summary>
    public async Task<RoleChangeResult> RevokeAsync(
        RoleActor actor,
        string roleCode,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (userId == actor.UserId)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.OwnRoles);
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var role = await FindRoleAsync(actor, roleCode, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.RoleNotFound);
        }

        var refusal = await CheckRankAsync(actor, role.Rank, at, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        // Every active row, including one whose validity window has lapsed: UX_UserRoles allows one
        // active row per user and role, and a lapsed one would block the next assignment.
        var assignment = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (assignment is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.AssignmentNotFound);
        }

        if (role.Rank == OwnerRank)
        {
            // Bumping the institution's version first takes its row's lock, so two revocations of
            // owners here queue: the second counts after the first has committed (no lost owner).
            await db.Institutions
                .Where(i => i.Id == actor.InstitutionId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PermissionsVersion, i => i.PermissionsVersion + 1), cancellationToken)
                .ConfigureAwait(false);

            // Any other owner-level assignment that is in force: another person's, or this person's
            // through a second owner-level role.
            var ownerLeft = await db.UserRoles
                .Where(ur => ur.InstitutionId == actor.InstitutionId
                             && ur.IsActive
                             && ur.Role.IsActive
                             && (ur.ValidFrom == null || ur.ValidFrom <= at)
                             && (ur.ValidTo == null || ur.ValidTo > at)
                             && !(ur.UserId == userId && ur.RoleId == role.Id)
                             && db.RoleLevels.Any(l => l.Id == ur.Role.RoleLevelId && l.Rank == OwnerRank))
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!ownerLeft)
            {
                return RoleChangeResult.Failed(RoleChangeFailure.LastOwner);
            }
        }

        // Deactivated rather than deleted: the temporal history keeps who held what, and when.
        assignment.IsActive = false;

        await BumpUserVersionAsync(userId, cancellationToken).ConfigureAwait(false);
        Record(actor, role.Id, role.Code, "Revoked", userId, at);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RoleChangeResult.Success;
    }

    // ------------------------------------------------------------------ checks

    private sealed record ManagedRole(int Id, string Code, byte LevelId, byte Rank);

    private sealed record ResolvedPermission(int Id, string Code);

    private IQueryable<UserRole> ActiveAssignments(Guid userId, int institutionId, DateTime at) =>
        db.UserRoles.Where(ur => ur.UserId == userId
                                 && ur.InstitutionId == institutionId
                                 && ur.IsActive
                                 && ur.Role.IsActive
                                 && (ur.ValidFrom == null || ur.ValidFrom <= at)
                                 && (ur.ValidTo == null || ur.ValidTo > at));

    /// <summary>Rule 1: the role's rank must be strictly more junior (a larger number) than the actor's most senior.</summary>
    private async Task<RoleChangeResult?> CheckRankAsync(RoleActor actor, byte roleRank, DateTime at, CancellationToken cancellationToken)
    {
        var actorRank = await ActiveAssignments(actor.UserId, actor.InstitutionId, at)
            .Join(db.RoleLevels, ur => ur.Role.RoleLevelId, l => l.Id, (ur, l) => (byte?)l.Rank)
            .MinAsync(cancellationToken)
            .ConfigureAwait(false);

        // Plan 04, Y3: platform support holds no role here and outranks every level.
        if (actorRank is null && await PlatformSupport.HoldsAsync(db, actor.UserId, cancellationToken).ConfigureAwait(false))
        {
            actorRank = 0;
        }

        if (actorRank is null)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.NoRank);
        }

        return roleRank > actorRank ? null : RoleChangeResult.Failed(RoleChangeFailure.RankNotJunior);
    }

    /// <summary>Rules 2 and 3: inside the level's ceiling, and held by the actor in this institution.</summary>
    private async Task<RoleChangeResult?> CheckGrantableAsync(
        RoleActor actor,
        byte levelId,
        IReadOnlyCollection<ResolvedPermission> permissions,
        CancellationToken cancellationToken)
    {
        if (permissions.Count == 0)
        {
            return null;
        }

        var ids = permissions.Select(p => p.Id).ToList();

        var inCeiling = await db.RoleLevelPermissions
            .Where(c => c.RoleLevelId == levelId && ids.Contains(c.PermissionId))
            .Select(c => c.PermissionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outside = permissions.Where(p => !inCeiling.Contains(p.Id)).Select(p => p.Code).Order(StringComparer.Ordinal).ToList();
        if (outside.Count > 0)
        {
            return RoleChangeResult.Failed(RoleChangeFailure.OutsideCeiling, outside);
        }

        var held = await db.EffectivePermissions
            .Where(e => e.UserId == actor.UserId && e.InstitutionId == actor.InstitutionId && ids.Contains(e.PermissionId))
            .Select(e => e.PermissionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var notHeld = permissions.Where(p => !held.Contains(p.Id)).Select(p => p.Code).Order(StringComparer.Ordinal).ToList();
        return notHeld.Count > 0 ? RoleChangeResult.Failed(RoleChangeFailure.NotHeld, notHeld) : null;
    }

    /// <summary>
    /// Rule 3 for handing a role to someone (assign, invite): the actor must hold everything the role
    /// would really grant here - its permissions inside the level's ceiling and the institution's
    /// enabled modules. Otherwise a manager could pass on what they lack, e.g. to a second account.
    /// </summary>
    private async Task<RoleChangeResult?> CheckRoleHeldAsync(RoleActor actor, ManagedRole role, CancellationToken cancellationToken)
    {
        var notHeld = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Join(db.RoleLevelPermissions.Where(c => c.RoleLevelId == role.LevelId), rp => rp.PermissionId, c => c.PermissionId, (rp, c) => rp.PermissionId)
            .Join(db.InstitutionPermissions.Where(ip => ip.InstitutionId == actor.InstitutionId), id => id, ip => ip.PermissionId, (id, ip) => ip)
            .Where(ip => !db.EffectivePermissions.Any(e => e.UserId == actor.UserId
                                                          && e.InstitutionId == actor.InstitutionId
                                                          && e.PermissionId == ip.PermissionId))
            .Select(ip => ip.PermissionCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notHeld.Count > 0
            ? RoleChangeResult.Failed(RoleChangeFailure.NotHeld, notHeld.Order(StringComparer.Ordinal).ToList())
            : null;
    }

    private async Task<(List<ResolvedPermission> Known, List<string> Unknown)> ResolveAsync(
        IReadOnlyCollection<string>? codes,
        CancellationToken cancellationToken)
    {
        var requested = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var known = await db.Permissions
            .AsNoTracking()
            .Where(p => requested.Contains(p.Code) && p.IsActive)
            .Select(p => new ResolvedPermission(p.Id, p.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unknown = requested
            .Where(c => !known.Any(k => string.Equals(k.Code, c, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToList();

        return (known, unknown);
    }

    private Task<ManagedRole?> FindRoleAsync(RoleActor actor, string roleCode, CancellationToken cancellationToken)
    {
        var code = roleCode.Trim().ToUpperInvariant();

        return db.Roles
            .AsNoTracking()
            .Where(r => r.InstitutionId == actor.InstitutionId && r.Code == code && r.IsActive)
            .Join(db.RoleLevels, r => r.RoleLevelId, l => l.Id, (r, l) => new ManagedRole(r.Id, r.Code, l.Id, l.Rank))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ effects

    /// <summary>The user's version is in their cache key, so their next permission check reloads.</summary>
    private Task<int> BumpUserVersionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
          .Where(u => u.Id == userId)
          .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionsVersion, u => u.PermissionsVersion + 1), cancellationToken);

    /// <summary>
    /// An outbox row in the same transaction, for whatever must follow a permissions change - audit,
    /// cache warming, telling the member. Ids only, no names or phone numbers.
    /// </summary>
    private void Record(RoleActor actor, int roleId, string roleCode, string change, Guid? targetUserId, DateTime at) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = ChangedEvent,
            AggregateType = "Role",
            AggregateId = roleId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Payload = JsonSerializer.Serialize(new
            {
                institutionId = actor.InstitutionId,
                roleCode,
                change,
                userId = targetUserId,
                by = actor.UserId,
            }),
            OccurredAt = at,
        });
}

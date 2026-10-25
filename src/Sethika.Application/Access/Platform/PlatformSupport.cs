using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Access.Platform;

/// <summary>
/// Whether a user may work inside any institution as platform support (plan 04, Y1): platform
/// staff who hold <see cref="Permission"/> globally. Read from <c>auth.vw_EffectivePermissions</c>,
/// like every permission decision, so a deny override or a revoked role takes it away at once.
/// </summary>
public static class PlatformSupport
{
    public const string Permission = "PLATFORM.INSTITUTIONS.EXECUTE";

    private const string PlatformStaff = "PlatformStaff";

    public static Task<bool> HoldsAsync(ISethikaDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.EffectivePermissions
            .Where(e => e.UserId == userId && e.InstitutionId == null && e.PermissionCode == Permission)
            .Join(db.Users.Where(u => u.UserType == PlatformStaff && !u.IsDeleted), e => e.UserId, u => u.Id, (e, u) => e)
            .AnyAsync(cancellationToken);
    }
}

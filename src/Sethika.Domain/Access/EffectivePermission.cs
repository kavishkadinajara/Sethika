namespace Sethika.Domain.Access;

/// <summary>
/// One permission a user may exercise in one institution, as <c>auth.vw_EffectivePermissions</c>
/// computes it: role grants inside their ceilings and the institution's enabled modules, plus grant
/// overrides, minus denies.
/// </summary>
/// <remarks>
/// A row of the view, read inside a command's transaction when a check must not trust the
/// permission cache. Never re-derived in C# (DECISIONS.md, RBAC).
/// </remarks>
public class EffectivePermission
{
    public Guid UserId { get; set; }
    public int? InstitutionId { get; set; }
    public int PermissionId { get; set; }
    public string PermissionCode { get; set; } = null!;
}

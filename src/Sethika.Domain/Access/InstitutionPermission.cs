namespace Sethika.Domain.Access;

/// <summary>
/// One permission that can take effect in one institution, as <c>auth.vw_InstitutionPermissions</c>
/// computes it: Institution scope, active, in a module the institution has switched on.
/// </summary>
/// <remarks>
/// Read inside a role command's transaction to learn what a role would really grant here, without
/// re-deriving the module rule in C#.
/// </remarks>
public class InstitutionPermission
{
    public int InstitutionId { get; set; }
    public int PermissionId { get; set; }
    public string PermissionCode { get; set; } = null!;
}

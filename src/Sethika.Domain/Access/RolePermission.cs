namespace Sethika.Domain.Access;

/// <summary>One permission a role grants. Maps <c>auth.RolePermissions</c>, a temporal table.</summary>
/// <remarks>
/// A row outside its role's level's ceiling grants nothing: <c>auth.vw_EffectivePermissions</c>
/// joins the ceiling, so the database decides, not this row.
/// </remarks>
public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public Guid? GrantedBy { get; set; }
}

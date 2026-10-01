namespace Sethika.Domain.Access;

/// <summary>One permission in the RBAC catalogue: a screen or element and an action. Maps <c>auth.Permissions</c>.</summary>
/// <remarks>Reference data seeded by <c>10_RbacCatalogue.sql</c>; the application only reads it.</remarks>
public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public bool RequiresMfa { get; set; }
    public bool IsActive { get; set; }
}

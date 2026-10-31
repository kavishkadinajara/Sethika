namespace Sethika.Domain.Access;

/// <summary>One permission inside one level's ceiling (plan A15). Maps <c>auth.RoleLevelPermissions</c>.</summary>
/// <remarks>Reference data defined by the platform; the application only reads it.</remarks>
public class RoleLevelPermission
{
    public byte RoleLevelId { get; set; }
    public int PermissionId { get; set; }
}

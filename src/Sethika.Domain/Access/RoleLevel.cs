namespace Sethika.Domain.Access;

/// <summary>A level a role sits at, with a rank - 1 is the most senior (plan A15). Maps <c>auth.RoleLevels</c>.</summary>
/// <remarks>Reference data defined by the platform; the application only reads it.</remarks>
public class RoleLevel
{
    public byte Id { get; set; }
    public string Scope { get; set; } = null!;
    public byte Rank { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
}

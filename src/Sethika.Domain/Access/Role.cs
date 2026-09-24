namespace Sethika.Domain.Access;

public class Role
{
    public int Id { get; set; }
    public int? InstitutionId { get; set; }
    public string Scope { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsTemplate { get; set; }
    public int? TemplateRoleId { get; set; }
    public short? InstitutionTypeId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<UserRole> UserRoles { get; } = [];
}

namespace Sethika.Domain.Access;

public class UserRole
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int RoleId { get; set; }
    public int? InstitutionId { get; set; }
    public Guid? AssignedBy { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

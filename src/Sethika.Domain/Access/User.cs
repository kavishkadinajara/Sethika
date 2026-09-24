namespace Sethika.Domain.Access;

public class User
{
    public Guid Id { get; set; }
    public string UserType { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneConfirmed { get; set; }
    public string? NicNumber { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string SecurityStamp { get; set; } = null!;
    public int PermissionsVersion { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool MfaRequired { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public string PreferredLanguage { get; set; } = null!;
    public long? ProfilePhotoFileId { get; set; }
    public string Status { get; set; } = null!;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<UserRole> UserRoles { get; } = [];
}

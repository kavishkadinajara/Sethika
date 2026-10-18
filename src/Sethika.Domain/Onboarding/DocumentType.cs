namespace Sethika.Domain.Onboarding;

/// <summary>A kind of document an application may carry. Maps <c>org.DocumentTypes</c>; reference data.</summary>
public class DocumentType
{
    public short Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Comma-separated, with dots: <c>.pdf,.jpg,.jpeg,.png</c>.</summary>
    public string AllowedExtensions { get; set; } = null!;

    public byte MaxSizeMb { get; set; }
    public bool RequiresReferenceNo { get; set; }
    public bool RequiresIssueDate { get; set; }
    public bool RequiresExpiryDate { get; set; }
    public bool IsActive { get; set; }
}

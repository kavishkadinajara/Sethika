namespace Sethika.Domain.Onboarding;

/// <summary>A question every reviewer answers. Maps <c>org.VerificationChecklistItems</c>; reference data.</summary>
public class VerificationChecklistItem
{
    public short Id { get; set; }

    /// <summary><see langword="null"/>: asked of every institution type.</summary>
    public short? InstitutionTypeId { get; set; }

    public string Code { get; set; } = null!;
    public string Question { get; set; } = null!;
    public bool IsMandatory { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; }
}

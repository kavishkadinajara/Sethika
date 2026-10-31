namespace Sethika.Domain.Onboarding;

/// <summary>A document an institution type must, or may, provide. Maps <c>org.InstitutionTypeRequiredDocuments</c>.</summary>
public class InstitutionTypeRequiredDocument
{
    public short InstitutionTypeId { get; set; }
    public short DocumentTypeId { get; set; }
    public bool IsMandatory { get; set; }
    public short SortOrder { get; set; }
    public string? Instructions { get; set; }
}

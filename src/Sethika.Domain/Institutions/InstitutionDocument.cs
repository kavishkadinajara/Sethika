namespace Sethika.Domain.Institutions;

/// <summary>
/// A verified document an institution holds. Maps <c>org.InstitutionDocuments</c>.
/// </summary>
/// <remarks>
/// Approval copies each verified application document here, pointing at the same <c>core.Files</c>
/// row - the bytes are not copied - and at the application document it came from.
/// </remarks>
public class InstitutionDocument
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public short DocumentTypeId { get; set; }
    public long FileId { get; set; }
    public int? SourceRequestDocumentId { get; set; }
    public string? ReferenceNo { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string Status { get; set; } = "Valid";
    public DateTime CreatedAt { get; set; }
}

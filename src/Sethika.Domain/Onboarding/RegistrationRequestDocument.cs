namespace Sethika.Domain.Onboarding;

/// <summary>An uploaded document on an application. Maps <c>org.RegistrationRequestDocuments</c>.</summary>
/// <remarks>
/// One live row per document type (<c>UX_ReqDoc_Live</c>): a replacement supersedes the old row rather
/// than deleting it, so the reviewer can see what was sent before.
/// </remarks>
public class RegistrationRequestDocument
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public short DocumentTypeId { get; set; }

    /// <summary><c>core.Files</c>: the bytes and their hash.</summary>
    public long FileId { get; set; }

    public string? ReferenceNo { get; set; }
    public string? IssuedBy { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string VerificationStatus { get; set; } = DocumentVerification.Pending;
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? ReviewerRemarks { get; set; }
    public DateTime UploadedAt { get; set; }

    public bool IsLive => VerificationStatus != DocumentVerification.Superseded;
}

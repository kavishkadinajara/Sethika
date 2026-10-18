namespace Sethika.Domain.Access;

/// <summary>A user's acceptance of one version of one legal document. Maps <c>auth.UserConsents</c>.</summary>
public class UserConsent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public short LegalDocumentId { get; set; }
    public DateTime AcceptedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? WithdrawnAt { get; set; }
}

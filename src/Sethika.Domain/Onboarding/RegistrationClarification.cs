namespace Sethika.Domain.Onboarding;

/// <summary>A reviewer's question and the applicant's answer. Maps <c>org.RegistrationRequestClarifications</c>.</summary>
public class RegistrationClarification
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Guid AskedBy { get; set; }
    public string Question { get; set; } = null!;
    public DateTime AskedAt { get; set; }
    public string? Response { get; set; }
    public long? ResponseFileId { get; set; }
    public Guid? RespondedBy { get; set; }
    public DateTime? RespondedAt { get; set; }

    public bool IsOpen => RespondedAt is null;
}

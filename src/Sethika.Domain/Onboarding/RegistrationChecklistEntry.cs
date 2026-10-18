namespace Sethika.Domain.Onboarding;

/// <summary>A reviewer's answer to one checklist item. Maps <c>org.RegistrationRequestChecklist</c>.</summary>
public class RegistrationChecklistEntry
{
    public int RequestId { get; set; }
    public short ChecklistItemId { get; set; }
    public bool IsPassed { get; set; }
    public Guid CheckedBy { get; set; }
    public DateTime CheckedAt { get; set; }
    public long? EvidenceFileId { get; set; }
    public string? Remarks { get; set; }
}

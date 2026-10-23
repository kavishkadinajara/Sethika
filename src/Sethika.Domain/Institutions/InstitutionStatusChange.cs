namespace Sethika.Domain.Institutions;

/// <summary>One change of an institution's status. Maps <c>org.InstitutionStatusHistory</c>.</summary>
public class InstitutionStatusChange
{
    public long Id { get; set; }
    public int InstitutionId { get; set; }

    /// <summary><see langword="null"/> when the institution is created.</summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;
    public string? Reason { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

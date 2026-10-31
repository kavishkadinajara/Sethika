namespace Sethika.Domain.Onboarding;

/// <summary>One transition of an application. Maps <c>org.RegistrationRequestStatusHistory</c>.</summary>
public class RegistrationStatusChange
{
    public long Id { get; set; }
    public int RequestId { get; set; }

    /// <summary><see langword="null"/> for the draft being started.</summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Comment { get; set; }
}

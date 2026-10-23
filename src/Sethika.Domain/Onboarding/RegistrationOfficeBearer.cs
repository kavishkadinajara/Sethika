namespace Sethika.Domain.Onboarding;

/// <summary>A trustee or committee member named on an application. Maps <c>org.RegistrationRequestOfficeBearers</c>.</summary>
public class RegistrationOfficeBearer
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public string FullName { get; set; } = null!;

    /// <summary>Masked in the database (<c>partial(2, "XXXXXXX", 2)</c>) for anyone without UNMASK.</summary>
    public string NicNumber { get; set; } = null!;

    public string Designation { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }

    /// <summary>May sign for the institution. Submitting needs at least one.</summary>
    public bool IsSignatory { get; set; }
}

namespace Sethika.Domain.Access;

/// <summary>What a donor account holds beyond the user row. Maps <c>auth.DonorProfiles</c>.</summary>
public class DonorProfile
{
    public Guid UserId { get; set; }
    public short? DistrictId { get; set; }
    public string CountryCode { get; set; } = null!;
    public int? ReligionLookupId { get; set; }
    public bool DefaultAnonymous { get; set; }
    public string? DefaultDisplayName { get; set; }
}

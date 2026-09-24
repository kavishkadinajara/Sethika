namespace Sethika.Domain.Institutions;

/// <summary>
/// A temple, kovil, mosque, church, elders' home or children's home.
/// The tenant that almost every other table hangs off.
/// </summary>
public class Institution
{
    public int Id { get; set; }
    public string InstitutionCode { get; set; } = null!;

    /// <summary>Used in public URLs; the internal Id is never exposed.</summary>
    public string Slug { get; set; } = null!;

    public int RegistrationRequestId { get; set; }
    public short InstitutionTypeId { get; set; }
    public string Name { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }
    public string OfficialRegistrationNo { get; set; } = null!;
    public string? Description { get; set; }
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public short DistrictId { get; set; }
    public short? DsDivisionId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    // org.Institutions.Location is a PERSISTED geography computed column. It is
    // deliberately not mapped: nothing reads it through EF yet, and mapping it
    // would drag NetTopologySuite into the build for no gain.

    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public short? ResidentCapacity { get; set; }
    public string? DietaryNotes { get; set; }
    public long? LogoFileId { get; set; }
    public long? CoverFileId { get; set; }
    public string Status { get; set; } = null!;
    public bool IsPubliclyListed { get; set; }
    public byte VerifiedBadgeLevel { get; set; }
    public DateOnly? NextReviewDue { get; set; }
    public int PermissionsVersion { get; set; }
    public Guid ApprovedBy { get; set; }
    public DateTime ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

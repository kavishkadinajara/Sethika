namespace Sethika.Domain.Services;

/// <summary>
/// A religious service an institution offers (Phase E, promise 1). Maps <c>booking.ServiceTypes</c>.
/// </summary>
/// <remarks>Institution-owned; nothing is seeded per faith (V4).</remarks>
public class ServiceType
{
    public int Id { get; set; }

    /// <summary>The service's address in URLs and APIs (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public string Name { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }
    public string? Description { get; set; }

    /// <summary>How long the service takes: a whole number of 15-minute blocks, 15 to 720.</summary>
    public short DurationMinutes { get; set; }

    /// <summary>Travel time added either side of an at-home service (V5).</summary>
    public short BufferMinutes { get; set; }

    public byte LeadersRequired { get; set; } = 1;

    /// <summary>A <see cref="ServiceLocation"/>: where the service may happen.</summary>
    public string LocationMode { get; set; } = ServiceLocation.AtInstitution;

    public short MinAdvanceDays { get; set; } = 1;
    public short MaxAdvanceDays { get; set; } = 180;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Whether a donor may ask for this service at <paramref name="location"/>.</summary>
    /// <param name="location"><see cref="ServiceLocation.AtInstitution"/> or <see cref="ServiceLocation.AtDonorPlace"/>.</param>
    public bool Offers(string location) =>
        location is ServiceLocation.AtInstitution or ServiceLocation.AtDonorPlace
        && (LocationMode == ServiceLocation.Either || LocationMode == location);

    /// <summary>The travel time either side, which only a visit to the donor's place needs (V5).</summary>
    public int BufferFor(string location) => location == ServiceLocation.AtDonorPlace ? BufferMinutes : 0;
}

/// <summary>Where a service happens: <c>ServiceTypes.LocationMode</c> and <c>ServiceAppointments.LocationMode</c>.</summary>
public static class ServiceLocation
{
    public const string AtInstitution = "AtInstitution";
    public const string AtDonorPlace = "AtDonorPlace";

    /// <summary>A service type that may be held in either place. Never an appointment's location.</summary>
    public const string Either = "Either";
}

namespace Sethika.Application.Services.Appointments;

/// <summary>A donor's request for a religious service, as the request form sends it (Phase E, 8.5).</summary>
/// <param name="ServiceType">The service type's <c>PublicId</c>.</param>
/// <param name="Date">The day, a Sri Lankan date.</param>
/// <param name="StartTime">Colombo time, on the 15-minute grid (V11).</param>
/// <param name="Location"><c>AtInstitution</c> or <c>AtDonorPlace</c>.</param>
/// <param name="Address">Where, for a visit to the donor's place.</param>
public sealed record RequestServiceCommand(
    Guid ServiceType,
    DateOnly Date,
    TimeOnly StartTime,
    string? Location,
    string? Address,
    string? Occasion,
    string? Note);

/// <summary>The outcome of a request: the appointment's number and status, or why not.</summary>
/// <param name="Error">An API error code (<c>ServiceErrors</c> or one of the constants here); <see langword="null"/> on success.</param>
public sealed record RequestServiceResult(string? Error, IReadOnlyList<string> Fields, string? AppointmentNo, string? Status)
{
    /// <summary>No service type with that id.</summary>
    public const string ServiceNotFound = "service_not_found";

    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>The signed-in account has no confirmed phone, which the appointment's texts go to.</summary>
    public const string PhoneRequired = "phone_required";

    public bool Succeeded => Error is null;

    internal static RequestServiceResult Ok(string appointmentNo, string status) => new(null, [], appointmentNo, status);

    internal static RequestServiceResult Failed(string error) => new(error, [], null, null);

    internal static RequestServiceResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null, null);
}

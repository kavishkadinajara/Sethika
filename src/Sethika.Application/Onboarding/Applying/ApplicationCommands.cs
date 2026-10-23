namespace Sethika.Application.Onboarding.Applying;

/// <summary>
/// An application's details as the applicant enters them. Codes, not ids: <see cref="InstitutionType"/>
/// is <c>TEMPLE</c>, <see cref="District"/> is <c>GLL</c>.
/// </summary>
public sealed record ApplicationDetails(
    string? InstitutionType,
    string? ProposedName,
    string? NameSi,
    string? NameTa,
    string? OfficialRegistrationNo,
    string? RegisteringAuthority,
    DateOnly? RegistrationDate,
    short? EstablishedYear,
    short? ResidentCapacity,
    string? Description,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? District,
    string? GnDivision,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    string? OfficePhone,
    string? OfficeEmail,
    string? Website,
    string? ApplicantFullName,
    string? ApplicantNic,
    string? ApplicantDesignation,
    string? ApplicantPhone,
    string? ApplicantEmail);

/// <summary>One trustee or committee member, as entered.</summary>
public sealed record OfficeBearerDetails(
    string? FullName,
    string? Nic,
    string? Designation,
    string? Phone,
    string? Email,
    bool IsSignatory);

/// <summary>An uploaded document and what the applicant says about it.</summary>
/// <param name="FileName">The client's name for the file: its extension is checked, and it is shown back.</param>
/// <param name="Length">The upload's length in bytes.</param>
/// <param name="Header">The file's first bytes (<c>UploadedFileRules.HeaderLength</c>).</param>
/// <param name="Content">The whole file, from the start.</param>
public sealed record DocumentUpload(
    string? FileName,
    long Length,
    ReadOnlyMemory<byte> Header,
    Stream Content,
    string? ReferenceNo,
    string? IssuedBy,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate);

/// <summary>The outcome of an applicant's change: the application's number, or why not.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="RequestNo">The application changed or created.</param>
public sealed record ApplicationResult(string? Error, IReadOnlyList<string> Fields, string? RequestNo)
{
    /// <summary>No application with that number among the caller's own.</summary>
    public const string NotFound = "registration_not_found";

    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>Already three applications that are neither decided nor withdrawn.</summary>
    public const string TooManyApplications = "too_many_applications";

    /// <summary>Someone changed the application since it was read; read it again.</summary>
    public const string Stale = "stale";

    /// <summary>The document type is not one this institution type provides.</summary>
    public const string DocumentTypeNotAllowed = "document_type_not_allowed";

    /// <summary>The declaration hash is not that of a current declaration text.</summary>
    public const string DeclarationRequired = "declaration_required";

    /// <summary>A live application already uses this registration number for this institution type.</summary>
    public const string RegistrationNoInUse = "registration_no_in_use";

    public bool Succeeded => Error is null;

    internal static ApplicationResult Ok(string requestNo) => new(null, [], requestNo);

    internal static ApplicationResult Failed(string error) => new(error, [], null);

    internal static ApplicationResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null);
}

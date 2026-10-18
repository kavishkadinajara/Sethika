namespace Sethika.Application.Abstractions;

/// <summary>An institution type an applicant can choose.</summary>
public sealed record InstitutionTypeOption(string Code, string Name, string? NameSi, string? NameTa, string Category);

/// <summary>A district an applicant can choose.</summary>
public sealed record DistrictOption(string Code, string Name, string? NameSi, string? NameTa);

/// <summary>A document an institution type provides, and what an upload of it must carry.</summary>
public sealed record RequiredDocumentOption(
    string InstitutionType,
    string Code,
    string Name,
    string? Description,
    string? Instructions,
    bool IsMandatory,
    short SortOrder,
    string AllowedExtensions,
    byte MaxSizeMb,
    bool RequiresReferenceNo,
    bool RequiresIssueDate,
    bool RequiresExpiryDate);

/// <summary>Everything the application form offers, in one read.</summary>
/// <param name="Declaration">The current institution declaration in the asked language, or English.</param>
public sealed record ApplicationLookups(
    IReadOnlyList<InstitutionTypeOption> InstitutionTypes,
    IReadOnlyList<DistrictOption> Districts,
    IReadOnlyList<RequiredDocumentOption> Documents,
    CurrentLegalDocument? Declaration);

/// <summary>One of the caller's applications, for the list.</summary>
public sealed record ApplicationSummary(
    string RequestNo,
    string ProposedName,
    string InstitutionType,
    string Status,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt);

/// <summary>An application as its applicant sees it. No reviewer's identity, no internal ids.</summary>
/// <remarks>Properties rather than a positional record: Dapper matches a constructor by exact types, and a
/// <c>date</c> column arrives as <see cref="DateTime"/>, not <see cref="DateOnly"/>.</remarks>
public sealed record ApplicationView
{
    public string RequestNo { get; init; } = "";
    public string Status { get; init; } = "";
    public string InstitutionType { get; init; } = "";
    public string ProposedName { get; init; } = "";
    public string? NameSi { get; init; }
    public string? NameTa { get; init; }
    public string OfficialRegistrationNo { get; init; } = "";
    public string RegisteringAuthority { get; init; } = "";
    public DateOnly RegistrationDate { get; init; }
    public short? EstablishedYear { get; init; }
    public short? ResidentCapacity { get; init; }
    public string? Description { get; init; }
    public string AddressLine1 { get; init; } = "";
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = "";
    public string District { get; init; } = "";
    public string? GnDivision { get; init; }
    public string? PostalCode { get; init; }
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }
    public string OfficePhone { get; init; } = "";
    public string? OfficeEmail { get; init; }
    public string? Website { get; init; }
    public string ApplicantFullName { get; init; } = "";
    public string ApplicantNic { get; init; } = "";
    public string ApplicantDesignation { get; init; } = "";
    public string ApplicantPhone { get; init; } = "";
    public string? ApplicantEmail { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? RejectReason { get; init; }
    public string? DecisionNote { get; init; }
    public string? InstitutionCode { get; init; }
    public string? InstitutionSlug { get; init; }

    public IReadOnlyList<ApplicationOfficeBearerView> OfficeBearers { get; init; } = [];
    public IReadOnlyList<ApplicationDocumentView> Documents { get; init; } = [];
    public IReadOnlyList<ApplicationQuestionView> Questions { get; init; } = [];
    public IReadOnlyList<ApplicationStatusView> History { get; init; } = [];
}

public sealed record ApplicationOfficeBearerView(string FullName, string Nic, string Designation, string Phone, string? Email, bool IsSignatory);

/// <summary>A live upload; superseded ones are not shown to the applicant.</summary>
public sealed record ApplicationDocumentView
{
    public string DocumentType { get; init; } = "";
    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }
    public string VerificationStatus { get; init; } = "";
    public string? ReviewerRemarks { get; init; }
    public string? ReferenceNo { get; init; }
    public string? IssuedBy { get; init; }
    public DateOnly? IssuedDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public DateTime UploadedAt { get; init; }
}

/// <summary>A reviewer's question, numbered from 1 in the order asked.</summary>
public sealed record ApplicationQuestionView(int Number, string Question, DateTime AskedAt, string? Response, DateTime? RespondedAt);

public sealed record ApplicationStatusView(string? FromStatus, string ToStatus, DateTime ChangedAt);

/// <summary>Where a stored file's bytes are, and how to hand them over.</summary>
public sealed record StoredFileLocation(string StorageKey, string ContentType, string OriginalName, long SizeBytes);

/// <summary>Dapper reads for the application flow (plan 02, 5.3). Every one runs under Row-Level Security.</summary>
public interface IOnboardingApplicationQueries
{
    /// <summary>The ids behind an institution type code and a district code; null for an unknown or retired one.</summary>
    Task<(short? InstitutionTypeId, short? DistrictId)> ResolveCodesAsync(
        string? institutionTypeCode,
        string? districtCode,
        CancellationToken cancellationToken = default);

    Task<ApplicationLookups> GetLookupsAsync(string language, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationSummary>> ListMineAsync(Guid applicantId, CancellationToken cancellationToken = default);

    Task<ApplicationView?> GetMineAsync(Guid applicantId, string requestNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live upload of a document type on an application: the applicant's own when
    /// <paramref name="applicantId"/> is given, any the session may see when it is not (the reviewer).
    /// </summary>
    Task<StoredFileLocation?> GetLiveDocumentFileAsync(
        Guid? applicantId,
        string requestNo,
        string documentTypeCode,
        CancellationToken cancellationToken = default);
}

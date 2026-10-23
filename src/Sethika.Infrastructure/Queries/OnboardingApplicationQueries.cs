using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;

namespace Sethika.Infrastructure.Queries;

/// <summary>
/// Dapper reads for the application flow, per the reads-use-Dapper rule. Row-Level Security already
/// limits applications to the caller's own; the applicant's queries also say so, so a platform
/// session reading through them cannot be handed someone else's by mistake.
/// </summary>
public sealed class OnboardingApplicationQueries(IDbConnectionFactory connections, ILegalDocumentQueries legal)
    : IOnboardingApplicationQueries
{
    private const string ResolveSql =
        """
        SELECT (SELECT t.Id FROM org.InstitutionTypes AS t WHERE t.Code = @institutionTypeCode AND t.IsActive = 1) AS InstitutionTypeId,
               (SELECT d.Id FROM core.Districts AS d WHERE d.Code = @districtCode) AS DistrictId;
        """;

    private const string LookupsSql =
        """
        SELECT t.Code AS Code, t.Name AS Name, t.NameSi AS NameSi, t.NameTa AS NameTa, t.Category AS Category
        FROM org.InstitutionTypes AS t
        WHERE t.IsActive = 1
        ORDER BY t.Name;

        SELECT d.Code AS Code, d.Name AS Name, d.NameSi AS NameSi, d.NameTa AS NameTa
        FROM core.Districts AS d
        ORDER BY d.Name;

        SELECT t.Code AS InstitutionType, dt.Code AS Code, dt.Name AS Name, dt.Description AS Description,
               r.Instructions AS Instructions, r.IsMandatory AS IsMandatory, r.SortOrder AS SortOrder,
               dt.AllowedExtensions AS AllowedExtensions, dt.MaxSizeMb AS MaxSizeMb,
               dt.RequiresReferenceNo AS RequiresReferenceNo, dt.RequiresIssueDate AS RequiresIssueDate,
               dt.RequiresExpiryDate AS RequiresExpiryDate
        FROM org.InstitutionTypeRequiredDocuments AS r
        JOIN org.InstitutionTypes AS t ON t.Id = r.InstitutionTypeId AND t.IsActive = 1
        JOIN org.DocumentTypes AS dt ON dt.Id = r.DocumentTypeId AND dt.IsActive = 1
        ORDER BY t.Code, r.SortOrder;
        """;

    private const string ListMineSql =
        """
        SELECT r.RequestNo AS RequestNo, r.ProposedName AS ProposedName, t.Code AS InstitutionType,
               r.Status AS Status, r.CreatedAt AS CreatedAt, r.SubmittedAt AS SubmittedAt, r.DecidedAt AS DecidedAt
        FROM org.RegistrationRequests AS r
        JOIN org.InstitutionTypes AS t ON t.Id = r.InstitutionTypeId
        WHERE r.ApplicantUserId = @applicantId
        ORDER BY r.CreatedAt DESC, r.Id DESC;
        """;

    // The applicant's own application, in any state; or, with no applicant (the reviewer), any that
    // is past its draft. One predicate in every statement, so the parts cannot disagree.
    private const string GetSql =
        """
        SELECT r.RequestNo AS RequestNo, r.Status AS Status, t.Code AS InstitutionType,
               r.ProposedName AS ProposedName, r.NameSi AS NameSi, r.NameTa AS NameTa,
               r.OfficialRegistrationNo AS OfficialRegistrationNo, r.RegisteringAuthority AS RegisteringAuthority,
               r.RegistrationDate AS RegistrationDate, r.EstablishedYear AS EstablishedYear,
               r.ResidentCapacity AS ResidentCapacity, r.Description AS Description,
               r.AddressLine1 AS AddressLine1, r.AddressLine2 AS AddressLine2, r.City AS City, d.Code AS District,
               r.GnDivision AS GnDivision, r.PostalCode AS PostalCode, r.Latitude AS Latitude, r.Longitude AS Longitude,
               r.OfficePhone AS OfficePhone, r.OfficeEmail AS OfficeEmail, r.Website AS Website,
               r.ApplicantFullName AS ApplicantFullName, r.ApplicantNic AS ApplicantNic,
               r.ApplicantDesignation AS ApplicantDesignation, r.ApplicantPhone AS ApplicantPhone,
               r.ApplicantEmail AS ApplicantEmail, r.CreatedAt AS CreatedAt, r.SubmittedAt AS SubmittedAt,
               r.DecidedAt AS DecidedAt, reason.Code AS RejectReason, r.DecisionNote AS DecisionNote,
               i.InstitutionCode AS InstitutionCode, i.Slug AS InstitutionSlug
        FROM org.RegistrationRequests AS r
        JOIN org.InstitutionTypes AS t ON t.Id = r.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = r.DistrictId
        LEFT JOIN core.LookupValues AS reason ON reason.Id = r.RejectReasonLookupId
        LEFT JOIN org.Institutions AS i ON i.Id = r.CreatedInstitutionId
        WHERE r.RequestNo = @requestNo AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'));

        SELECT b.FullName AS FullName, b.NicNumber AS Nic, b.Designation AS Designation, b.Phone AS Phone,
               b.Email AS Email, b.IsSignatory AS IsSignatory
        FROM org.RegistrationRequestOfficeBearers AS b
        JOIN org.RegistrationRequests AS r ON r.Id = b.RequestId
        WHERE r.RequestNo = @requestNo AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'))
        ORDER BY b.Id;

        SELECT dt.Code AS DocumentType, f.OriginalName AS FileName, f.SizeBytes AS SizeBytes,
               doc.VerificationStatus AS VerificationStatus, doc.ReviewerRemarks AS ReviewerRemarks,
               doc.ReferenceNo AS ReferenceNo, doc.IssuedBy AS IssuedBy, doc.IssuedDate AS IssuedDate,
               doc.ExpiryDate AS ExpiryDate, doc.UploadedAt AS UploadedAt
        FROM org.RegistrationRequestDocuments AS doc
        JOIN org.RegistrationRequests AS r ON r.Id = doc.RequestId
        JOIN org.DocumentTypes AS dt ON dt.Id = doc.DocumentTypeId
        JOIN core.Files AS f ON f.Id = doc.FileId
        WHERE r.RequestNo = @requestNo AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'))
          AND doc.VerificationStatus <> 'Superseded'
        ORDER BY dt.Code;

        SELECT CAST(ROW_NUMBER() OVER (ORDER BY c.AskedAt, c.Id) AS INT) AS Number, c.Question AS Question,
               c.AskedAt AS AskedAt, c.Response AS Response, c.RespondedAt AS RespondedAt
        FROM org.RegistrationRequestClarifications AS c
        JOIN org.RegistrationRequests AS r ON r.Id = c.RequestId
        WHERE r.RequestNo = @requestNo AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'))
        ORDER BY Number;

        SELECT h.FromStatus AS FromStatus, h.ToStatus AS ToStatus, h.ChangedAt AS ChangedAt
        FROM org.RegistrationRequestStatusHistory AS h
        JOIN org.RegistrationRequests AS r ON r.Id = h.RequestId
        WHERE r.RequestNo = @requestNo AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'))
        ORDER BY h.Id;
        """;

    private const string LiveDocumentFileSql =
        """
        SELECT f.StorageKey AS StorageKey, f.ContentType AS ContentType, f.OriginalName AS OriginalName, f.SizeBytes AS SizeBytes
        FROM org.RegistrationRequests AS r
        JOIN org.RegistrationRequestDocuments AS doc ON doc.RequestId = r.Id AND doc.VerificationStatus <> 'Superseded'
        JOIN org.DocumentTypes AS dt ON dt.Id = doc.DocumentTypeId AND dt.Code = @documentTypeCode
        JOIN core.Files AS f ON f.Id = doc.FileId AND f.IsDeleted = 0
        WHERE r.RequestNo = @requestNo
          AND (r.ApplicantUserId = @applicantId OR (@applicantId IS NULL AND r.Status <> 'Draft'));
        """;

    public async Task<(short? InstitutionTypeId, short? DistrictId)> ResolveCodesAsync(
        string? institutionTypeCode,
        string? districtCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleAsync<(short?, short?)>(new CommandDefinition(
            ResolveSql,
            new { institutionTypeCode, districtCode },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ApplicationLookups> GetLookupsAsync(string language, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            LookupsSql,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var types = (await grid.ReadAsync<InstitutionTypeOption>().ConfigureAwait(false)).ToList();
        var districts = (await grid.ReadAsync<DistrictOption>().ConfigureAwait(false)).ToList();
        var documents = (await grid.ReadAsync<RequiredDocumentOption>().ConfigureAwait(false)).ToList();

        var declaration = await legal
            .GetCurrentAsync(language, [LegalDocumentCodes.InstitutionDeclaration], cancellationToken)
            .ConfigureAwait(false);

        return new ApplicationLookups(types, districts, documents, declaration.SingleOrDefault());
    }

    public async Task<IReadOnlyList<ApplicationSummary>> ListMineAsync(Guid applicantId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ApplicationSummary>(new CommandDefinition(
            ListMineSql,
            new { applicantId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    public Task<ApplicationView?> GetMineAsync(Guid applicantId, string requestNo, CancellationToken cancellationToken = default) =>
        GetAsync(applicantId, requestNo, cancellationToken);

    public Task<ApplicationView?> GetSubmittedAsync(string requestNo, CancellationToken cancellationToken = default) =>
        GetAsync(null, requestNo, cancellationToken);

    private async Task<ApplicationView?> GetAsync(Guid? applicantId, string requestNo, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            GetSql,
            new { applicantId, requestNo },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var view = await grid.ReadSingleOrDefaultAsync<ApplicationView>().ConfigureAwait(false);
        if (view is null)
        {
            return null;
        }

        return view with
        {
            OfficeBearers = [.. await grid.ReadAsync<ApplicationOfficeBearerView>().ConfigureAwait(false)],
            Documents = [.. await grid.ReadAsync<ApplicationDocumentView>().ConfigureAwait(false)],
            Questions = [.. await grid.ReadAsync<ApplicationQuestionView>().ConfigureAwait(false)],
            History = [.. await grid.ReadAsync<ApplicationStatusView>().ConfigureAwait(false)],
        };
    }

    public async Task<StoredFileLocation?> GetLiveDocumentFileAsync(
        Guid? applicantId,
        string requestNo,
        string documentTypeCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<StoredFileLocation>(new CommandDefinition(
            LiveDocumentFileSql,
            new { applicantId, requestNo, documentTypeCode },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

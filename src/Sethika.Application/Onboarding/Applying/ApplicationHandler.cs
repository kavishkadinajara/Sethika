using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Files;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;

namespace Sethika.Application.Onboarding.Applying;

/// <summary>
/// What an applicant does to their own applications (plan 02, 5.3): start one, fill it in, upload
/// its documents, submit it, answer the reviewer, withdraw it.
/// </summary>
/// <remarks>
/// Every method acts for the signed-in applicant on an application Row-Level Security lets them
/// see, which is only their own; another person's number reads as not found, never as forbidden,
/// so the answer says nothing about whose it is. The aggregate decides what is allowed when; this
/// checks the input, resolves codes, and saves. A save that loses a race on the application's
/// RowVersion answers <see cref="ApplicationResult.Stale"/>.
/// </remarks>
public sealed class ApplicationHandler(
    ISethikaDbContext db,
    IOnboardingApplicationQueries queries,
    IRegistrationLocks locks,
    INumberSeries numbers,
    IFileStorage storage,
    IPhoneNumberNormalizer phones,
    TimeProvider timeProvider)
{
    /// <summary>Applications neither decided nor withdrawn that one person may hold at once.</summary>
    public const int MaxOpenApplications = 3;

    public const string SubmittedEvent = "RegistrationSubmitted";

    private const string ActiveRegistrationNoIndex = "UX_RegReq_ActiveRegNo";

    /// <summary>Starts a draft with its details, numbered from the request series.</summary>
    public async Task<ApplicationResult> CreateAsync(Guid applicantId, ApplicationDetails details, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (tidy, typeId, districtId, invalid) = await CheckDetailsAsync(details, now, cancellationToken).ConfigureAwait(false);
        if (invalid.Count > 0)
        {
            return ApplicationResult.Invalid(invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // One person's new applications are counted and made one at a time, so two at once cannot
        // both find room under the limit.
        await locks.LockApplicantAsync(applicantId, cancellationToken).ConfigureAwait(false);

        var open = await db.RegistrationRequests
            .CountAsync(
                r => r.ApplicantUserId == applicantId
                     && r.Status != RegistrationStatus.Approved
                     && r.Status != RegistrationStatus.Rejected
                     && r.Status != RegistrationStatus.Withdrawn,
                cancellationToken)
            .ConfigureAwait(false);

        if (open >= MaxOpenApplications)
        {
            return ApplicationResult.Failed(ApplicationResult.TooManyApplications);
        }

        var requestNo = await numbers
            .NextAsync(NumberSeriesDefinition.RegistrationRequest, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var request = RegistrationRequest.StartDraft(requestNo, applicantId, now);
        Apply(tidy, typeId, districtId, request);
        db.RegistrationRequests.Add(request);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ApplicationResult.Ok(requestNo);
    }

    /// <summary>Replaces the details, while the application is a draft or has questions open.</summary>
    public async Task<ApplicationResult> UpdateDetailsAsync(Guid applicantId, string requestNo, ApplicationDetails details, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (tidy, typeId, districtId, invalid) = await CheckDetailsAsync(details, now, cancellationToken).ConfigureAwait(false);
        if (invalid.Count > 0)
        {
            return ApplicationResult.Invalid(invalid);
        }

        try
        {
            return await ChangeAsync(applicantId, requestNo, request =>
            {
                var allowed = request.BeginApplicantEdit(applicantId, now);
                if (allowed.Succeeded)
                {
                    Apply(tidy, typeId, districtId, request);
                }

                return Task.FromResult(allowed);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == ActiveRegistrationNoIndex)
        {
            // InfoRequested is a live status (the index's filter), so an edit while answering a
            // question can move onto another live application's number or type, as a submit can (QA DEF-004).
            return ApplicationResult.Failed(ApplicationResult.RegistrationNoInUse);
        }
    }

    /// <summary>Replaces the office bearers as a set.</summary>
    public async Task<ApplicationResult> ReplaceOfficeBearersAsync(
        Guid applicantId,
        string requestNo,
        IReadOnlyList<OfficeBearerDetails>? bearers,
        CancellationToken cancellationToken = default)
    {
        var (tidy, invalid) = ApplicationInput.Check(bearers, phones);
        if (invalid.Count > 0)
        {
            return ApplicationResult.Invalid(invalid);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await ChangeAsync(applicantId, requestNo, request => Task.FromResult(request.ReplaceOfficeBearers(
            [.. tidy.Select(b => new RegistrationOfficeBearer
            {
                FullName = b.FullName!,
                NicNumber = b.Nic!,
                Designation = b.Designation!,
                Phone = b.Phone!,
                Email = b.Email,
                IsSignatory = b.IsSignatory,
            })],
            applicantId,
            now)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores an upload and attaches it as the application's document of a type, superseding the
    /// one before it. The file is stored first and deleted again if the save does not happen.
    /// </summary>
    public async Task<ApplicationResult> AttachDocumentAsync(
        Guid applicantId,
        string requestNo,
        string documentTypeCode,
        DocumentUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var request = await db.LoadRegistrationAsync(requestNo, cancellationToken).ConfigureAwait(false);
        if (request is null || request.ApplicantUserId != applicantId)
        {
            return ApplicationResult.Failed(ApplicationResult.NotFound);
        }

        // Before storing anything: a refusal the aggregate would give anyway costs no disk.
        if (!request.IsEditableByApplicant)
        {
            return ApplicationResult.Failed(RegistrationErrors.NotEditable);
        }

        var code = documentTypeCode.Trim().ToUpperInvariant();
        var type = await db.DocumentTypes
            .Where(t => t.Code == code && t.IsActive)
            .Where(t => db.InstitutionTypeRequiredDocuments.Any(r => r.InstitutionTypeId == request.InstitutionTypeId && r.DocumentTypeId == t.Id))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (type is null)
        {
            return ApplicationResult.Failed(ApplicationResult.DocumentTypeNotAllowed);
        }

        var invalid = new List<string>();
        var referenceNo = upload.ReferenceNo?.Trim();
        if ((type.RequiresReferenceNo && string.IsNullOrEmpty(referenceNo)) || referenceNo is { Length: > 80 } || referenceNo?.Any(c => !char.IsAscii(c)) == true)
        {
            invalid.Add("referenceNo");
        }

        var issuedBy = upload.IssuedBy?.Trim();
        if (issuedBy is { Length: > 200 })
        {
            invalid.Add("issuedBy");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if ((type.RequiresIssueDate && upload.IssuedDate is null) || upload.IssuedDate > today)
        {
            invalid.Add("issuedDate");
        }

        // CK_ReqDoc_Dates: an expiry, when given, comes after the issue.
        if ((type.RequiresExpiryDate && upload.ExpiryDate is null) || upload.ExpiryDate <= upload.IssuedDate)
        {
            invalid.Add("expiryDate");
        }

        if (invalid.Count > 0)
        {
            return ApplicationResult.Invalid(invalid);
        }

        var check = UploadedFileRules.Check(upload.FileName, upload.Length, upload.Header.Span, type.AllowedExtensions, type.MaxSizeMb);
        if (!check.IsAcceptable)
        {
            return ApplicationResult.Failed(check.Error!);
        }

        var saved = await storage.SaveAsync(upload.Content, UploadedFileRules.MaxBytes(type.MaxSizeMb), cancellationToken).ConfigureAwait(false);
        if (saved is null)
        {
            return ApplicationResult.Failed(UploadedFileRules.TooLarge);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var file = new StoredFile
            {
                PublicId = Guid.NewGuid(),
                StorageProvider = storage.ProviderName,
                StorageKey = saved.StorageKey,
                OriginalName = OriginalName(upload.FileName, check.Extension!),
                ContentType = check.ContentType!,
                SizeBytes = saved.SizeBytes,
                Sha256Hash = saved.Sha256,
                UploadedBy = applicantId,
                UploadedAt = now,
            };
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var attached = request.AttachDocument(
                new RegistrationRequestDocument
                {
                    DocumentTypeId = type.Id,
                    FileId = file.Id,
                    ReferenceNo = string.IsNullOrEmpty(referenceNo) ? null : referenceNo,
                    IssuedBy = string.IsNullOrEmpty(issuedBy) ? null : issuedBy,
                    IssuedDate = upload.IssuedDate,
                    ExpiryDate = upload.ExpiryDate,
                },
                applicantId,
                now);

            if (!attached.Succeeded)
            {
                await storage.DeleteAsync(saved.StorageKey, cancellationToken).ConfigureAwait(false);
                return ApplicationResult.Failed(attached.Error!);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(saved.StorageKey, CancellationToken.None).ConfigureAwait(false);
            return ApplicationResult.Failed(ApplicationResult.Stale);
        }
        catch
        {
            await storage.DeleteAsync(saved.StorageKey, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return ApplicationResult.Ok(request.RequestNo);
    }

    /// <summary>
    /// Submits the draft with the declaration whose text the applicant was shown, identified by its
    /// <c>ContentSha256</c> exactly as signup's consent is (plan 01, Q4).
    /// </summary>
    public async Task<ApplicationResult> SubmitAsync(
        Guid applicantId,
        string requestNo,
        string? declarationSha256,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        var hash = declarationSha256?.Trim().ToLowerInvariant();
        var declaration = await db.LegalDocuments
            .Where(d => d.IsCurrent && d.Code == LegalDocumentCodes.InstitutionDeclaration && d.ContentSha256 == hash)
            .Select(d => (short?)d.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (declaration is not { } declarationId)
        {
            return ApplicationResult.Failed(ApplicationResult.DeclarationRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            return await ChangeAsync(applicantId, requestNo, async request =>
            {
                var mandatory = await db.InstitutionTypeRequiredDocuments
                    .Where(d => d.InstitutionTypeId == request.InstitutionTypeId && d.IsMandatory)
                    .Select(d => d.DocumentTypeId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var submitted = request.Submit(mandatory, declarationId, clientIp, applicantId, now);
                if (submitted.Succeeded)
                {
                    Notify(request, SubmittedEvent, now);
                }

                return submitted;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException ex) when (ex.KeyName == ActiveRegistrationNoIndex)
        {
            // Another live application - anyone's, which RLS keeps this caller from seeing - already
            // uses this registration number for this type. Only the index can see the clash.
            return ApplicationResult.Failed(ApplicationResult.RegistrationNoInUse);
        }
    }

    /// <summary>Answers every open question and returns the application to review.</summary>
    public Task<ApplicationResult> AnswerAsync(Guid applicantId, string requestNo, IReadOnlyList<string>? answers, CancellationToken cancellationToken = default)
    {
        if (answers is null || answers.Any(a => a is { Length: > 2000 }))
        {
            return Task.FromResult(ApplicationResult.Invalid(["answers"]));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return ChangeAsync(applicantId, requestNo, request => Task.FromResult(request.Answer(answers, applicantId, now)), cancellationToken);
    }

    /// <summary>Withdraws the application. Final.</summary>
    public Task<ApplicationResult> WithdrawAsync(Guid applicantId, string requestNo, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return ChangeAsync(applicantId, requestNo, request => Task.FromResult(request.Withdraw(applicantId, now)), cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Loads the caller's application, lets <paramref name="change"/> act on it, and saves.</summary>
    private async Task<ApplicationResult> ChangeAsync(
        Guid applicantId,
        string requestNo,
        Func<RegistrationRequest, Task<RegistrationResult>> change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var request = await db.LoadRegistrationAsync(requestNo, cancellationToken).ConfigureAwait(false);
        if (request is null || request.ApplicantUserId != applicantId)
        {
            return ApplicationResult.Failed(ApplicationResult.NotFound);
        }

        var result = await change(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ApplicationResult.Failed(result.Error!);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApplicationResult.Failed(ApplicationResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ApplicationResult.Ok(request.RequestNo);
    }

    private async Task<(ApplicationDetails Tidy, short TypeId, short DistrictId, IReadOnlyList<string> Invalid)> CheckDetailsAsync(
        ApplicationDetails details,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        var (tidy, invalid) = ApplicationInput.Check(details, phones, DateOnly.FromDateTime(now));
        var (typeId, districtId) = await queries.ResolveCodesAsync(tidy.InstitutionType, tidy.District, cancellationToken).ConfigureAwait(false);

        var all = invalid.ToList();
        if (typeId is null && !all.Contains("institutionType"))
        {
            all.Add("institutionType");
        }

        if (districtId is null && !all.Contains("district"))
        {
            all.Add("district");
        }

        return (tidy, typeId ?? 0, districtId ?? 0, all);
    }

    private static void Apply(ApplicationDetails d, short typeId, short districtId, RegistrationRequest r)
    {
        r.InstitutionTypeId = typeId;
        r.ProposedName = d.ProposedName!;
        r.NameSi = d.NameSi;
        r.NameTa = d.NameTa;
        r.OfficialRegistrationNo = d.OfficialRegistrationNo!;
        r.RegisteringAuthority = d.RegisteringAuthority!;
        r.RegistrationDate = d.RegistrationDate!.Value;
        r.EstablishedYear = d.EstablishedYear;
        r.ResidentCapacity = d.ResidentCapacity;
        r.Description = d.Description;
        r.AddressLine1 = d.AddressLine1!;
        r.AddressLine2 = d.AddressLine2;
        r.City = d.City!;
        r.DistrictId = districtId;
        r.GnDivision = d.GnDivision;
        r.PostalCode = d.PostalCode;
        r.Latitude = d.Latitude!.Value;
        r.Longitude = d.Longitude!.Value;
        r.OfficePhone = d.OfficePhone!;
        r.OfficeEmail = d.OfficeEmail;
        r.Website = d.Website;
        r.ApplicantFullName = d.ApplicantFullName!;
        r.ApplicantNic = d.ApplicantNic!;
        r.ApplicantDesignation = d.ApplicantDesignation!;
        r.ApplicantPhone = d.ApplicantPhone!;
        r.ApplicantEmail = d.ApplicantEmail;
    }

    /// <summary>
    /// The uploader's name, kept only for showing and offering on download, cut to the column and
    /// stripped of anything that could break a header. A name with nothing left gets a plain one.
    /// </summary>
    private static string OriginalName(string? fileName, string extension)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        name = new string([.. name.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/'))]).Trim();

        if (name.Length == 0)
        {
            return "document" + extension;
        }

        return name.Length <= 255 ? name : name[^255..];
    }

    /// <summary>An outbox message for the applicant's SMS (5.7).</summary>
    private void Notify(RegistrationRequest request, string eventType, DateTime now) =>
        db.OutboxMessages.Add(OnboardingOutbox.Message(request, eventType, now));
}

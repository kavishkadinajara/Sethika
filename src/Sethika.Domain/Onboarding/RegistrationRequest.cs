namespace Sethika.Domain.Onboarding;

/// <summary>
/// An institution's application to join Sethika (plan 02, B3). Maps <c>org.RegistrationRequests</c>.
/// </summary>
/// <remarks>
/// The aggregate owns its status: every transition is a method here that checks who is asking and
/// from which status, refuses with a <see cref="RegistrationErrors"/> code otherwise, and writes a
/// <see cref="StatusHistory"/> row. The status and decision fields have private setters, so a
/// handler cannot move an application by assignment.
///
/// Every change, including one that only touches a child row, also sets <see cref="UpdatedAt"/>.
/// That writes the parent row, so its <see cref="RowVersion"/> is checked on save: two reviewers
/// verifying documents on the same application cannot both win.
///
/// What the aggregate cannot know - which documents the type requires, which checklist items are
/// mandatory - the caller passes in from the reference tables.
/// </remarks>
public class RegistrationRequest
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = null!;
    public Guid ApplicantUserId { get; set; }
    public short InstitutionTypeId { get; set; }

    public string ProposedName { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }
    public string OfficialRegistrationNo { get; set; } = null!;
    public string RegisteringAuthority { get; set; } = null!;
    public DateOnly RegistrationDate { get; set; }
    public short? EstablishedYear { get; set; }
    public short? ResidentCapacity { get; set; }
    public string? Description { get; set; }

    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public short DistrictId { get; set; }
    public short? DsDivisionId { get; set; }
    public string? GnDivision { get; set; }
    public string? PostalCode { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    public string OfficePhone { get; set; } = null!;
    public string? OfficeEmail { get; set; }
    public string? Website { get; set; }

    public string ApplicantFullName { get; set; } = null!;

    /// <summary>Masked in the database for anyone without UNMASK (plan 02, B12).</summary>
    public string ApplicantNic { get; set; } = null!;

    public string ApplicantDesignation { get; set; } = null!;
    public string ApplicantPhone { get; set; } = null!;
    public string? ApplicantEmail { get; set; }

    public short? DeclarationDocumentId { get; private set; }
    public DateTime? DeclarationAcceptedAt { get; private set; }
    public string? DeclarationIp { get; private set; }

    public short? RequestedPlanId { get; set; }
    public byte? RiskScore { get; set; }
    public string? RiskFlags { get; set; }

    public string Status { get; private set; } = RegistrationStatus.Draft;
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? SlaDueAt { get; set; }
    public Guid? AssignedReviewerId { get; set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public int? RejectReasonLookupId { get; private set; }
    public string? DecisionNote { get; private set; }
    public int? CreatedInstitutionId { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<RegistrationRequestDocument> Documents { get; } = new List<RegistrationRequestDocument>();
    public ICollection<RegistrationOfficeBearer> OfficeBearers { get; } = new List<RegistrationOfficeBearer>();
    public ICollection<RegistrationChecklistEntry> ChecklistEntries { get; } = new List<RegistrationChecklistEntry>();
    public ICollection<RegistrationClarification> Clarifications { get; } = new List<RegistrationClarification>();
    public ICollection<RegistrationStatusChange> StatusHistory { get; } = new List<RegistrationStatusChange>();

    /// <summary>The applicant may change details, office bearers and documents only now.</summary>
    public bool IsEditableByApplicant => Status is RegistrationStatus.Draft or RegistrationStatus.InfoRequested;

    /// <summary>The live (not superseded) upload for each document type.</summary>
    public IEnumerable<RegistrationRequestDocument> LiveDocuments => Documents.Where(d => d.IsLive);

    /// <summary>Questions still waiting for the applicant, oldest first.</summary>
    public IReadOnlyList<RegistrationClarification> OpenQuestions =>
        [.. Clarifications.Where(c => c.IsOpen).OrderBy(c => c.AskedAt).ThenBy(c => c.Id)];

    /// <summary>A new draft. The caller fills in the details before saving it.</summary>
    public static RegistrationRequest StartDraft(string requestNo, Guid applicantUserId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNo);

        var request = new RegistrationRequest
        {
            RequestNo = requestNo,
            ApplicantUserId = applicantUserId,
            CreatedAt = now,
        };

        request.StatusHistory.Add(new RegistrationStatusChange
        {
            FromStatus = null,
            ToStatus = RegistrationStatus.Draft,
            ChangedBy = applicantUserId,
            ChangedAt = now,
        });

        return request;
    }

    // ------------------------------------------------------------------ the applicant

    /// <summary>Before the applicant changes the details: allowed only on their own, editable application.</summary>
    public RegistrationResult BeginApplicantEdit(Guid userId, DateTime now)
    {
        var refused = RefuseUnlessEditableBy(userId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        Touch(now);
        return RegistrationResult.Ok;
    }

    /// <summary>Replaces the office bearers as a set; the API never exposes their row ids.</summary>
    public RegistrationResult ReplaceOfficeBearers(IReadOnlyCollection<RegistrationOfficeBearer> bearers, Guid userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(bearers);

        var refused = RefuseUnlessEditableBy(userId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        // UQ_OfficeBearer_Nic would refuse it too, but only as a database error at save.
        if (bearers.GroupBy(b => b.NicNumber.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            return RegistrationResult.Fail(RegistrationErrors.OfficeBearerDuplicate);
        }

        OfficeBearers.Clear();
        foreach (var bearer in bearers)
        {
            OfficeBearers.Add(bearer);
        }

        Touch(now);
        return RegistrationResult.Ok;
    }

    /// <summary>
    /// Adds an upload of <paramref name="document"/>'s type, superseding the live one of that type,
    /// whose verification no longer counts: a replaced document is checked again.
    /// </summary>
    public RegistrationResult AttachDocument(RegistrationRequestDocument document, Guid userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(document);

        var refused = RefuseUnlessEditableBy(userId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        foreach (var live in LiveDocuments.Where(d => d.DocumentTypeId == document.DocumentTypeId).ToList())
        {
            live.VerificationStatus = DocumentVerification.Superseded;
        }

        document.VerificationStatus = DocumentVerification.Pending;
        document.VerifiedBy = null;
        document.VerifiedAt = null;
        document.ReviewerRemarks = null;
        document.UploadedAt = now;
        Documents.Add(document);

        Touch(now);
        return RegistrationResult.Ok;
    }

    /// <summary>Draft → Submitted, with the declaration the applicant accepted.</summary>
    /// <param name="mandatoryDocumentTypeIds">The document types this institution type must provide.</param>
    /// <param name="declarationDocumentId">The <c>core.LegalDocuments</c> row whose text was accepted.</param>
    /// <param name="clientIp">Where the acceptance came from (plan 01, A18).</param>
    public RegistrationResult Submit(
        IReadOnlyCollection<short> mandatoryDocumentTypeIds,
        short declarationDocumentId,
        string? clientIp,
        Guid userId,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(mandatoryDocumentTypeIds);

        if (userId != ApplicantUserId)
        {
            return RegistrationResult.Fail(RegistrationErrors.NotApplicant);
        }

        if (Status != RegistrationStatus.Draft)
        {
            return RegistrationResult.Fail(RegistrationErrors.InvalidTransition);
        }

        var uploaded = LiveDocuments.Select(d => d.DocumentTypeId).ToHashSet();
        if (!mandatoryDocumentTypeIds.All(uploaded.Contains))
        {
            return RegistrationResult.Fail(RegistrationErrors.DocumentsMissing);
        }

        if (OfficeBearers.Count == 0)
        {
            return RegistrationResult.Fail(RegistrationErrors.OfficeBearerRequired);
        }

        if (!OfficeBearers.Any(b => b.IsSignatory))
        {
            return RegistrationResult.Fail(RegistrationErrors.SignatoryRequired);
        }

        DeclarationDocumentId = declarationDocumentId;
        DeclarationAcceptedAt = now;
        DeclarationIp = clientIp;
        SubmittedAt = now;

        MoveTo(RegistrationStatus.Submitted, userId, now);
        return RegistrationResult.Ok;
    }

    /// <summary>InfoRequested → UnderReview: every open question answered, in the order <see cref="OpenQuestions"/> lists them.</summary>
    public RegistrationResult Answer(IReadOnlyList<string> responses, Guid userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(responses);

        if (userId != ApplicantUserId)
        {
            return RegistrationResult.Fail(RegistrationErrors.NotApplicant);
        }

        if (Status != RegistrationStatus.InfoRequested)
        {
            return RegistrationResult.Fail(RegistrationErrors.InvalidTransition);
        }

        var open = OpenQuestions;
        if (responses.Count != open.Count || responses.Any(string.IsNullOrWhiteSpace))
        {
            return RegistrationResult.Fail(RegistrationErrors.QuestionsUnanswered);
        }

        for (var i = 0; i < open.Count; i++)
        {
            open[i].Response = responses[i].Trim();
            open[i].RespondedBy = userId;
            open[i].RespondedAt = now;
        }

        MoveTo(RegistrationStatus.UnderReview, userId, now);
        return RegistrationResult.Ok;
    }

    /// <summary>Draft, Submitted or InfoRequested → Withdrawn. Final.</summary>
    public RegistrationResult Withdraw(Guid userId, DateTime now, string? comment = null)
    {
        if (userId != ApplicantUserId)
        {
            return RegistrationResult.Fail(RegistrationErrors.NotApplicant);
        }

        if (Status is not (RegistrationStatus.Draft or RegistrationStatus.Submitted or RegistrationStatus.InfoRequested))
        {
            return RegistrationResult.Fail(RegistrationErrors.InvalidTransition);
        }

        MoveTo(RegistrationStatus.Withdrawn, userId, now, comment);
        return RegistrationResult.Ok;
    }

    // ------------------------------------------------------------------ the reviewer

    /// <summary>Submitted → UnderReview.</summary>
    public RegistrationResult StartReview(Guid reviewerId, DateTime now)
    {
        if (reviewerId == ApplicantUserId)
        {
            return RegistrationResult.Fail(RegistrationErrors.OwnApplication);
        }

        if (Status != RegistrationStatus.Submitted)
        {
            return RegistrationResult.Fail(RegistrationErrors.InvalidTransition);
        }

        MoveTo(RegistrationStatus.UnderReview, reviewerId, now);
        return RegistrationResult.Ok;
    }

    /// <summary>Marks the live document of a type verified or rejected, while under review.</summary>
    public RegistrationResult VerifyDocument(short documentTypeId, bool verified, string? remarks, Guid reviewerId, DateTime now)
    {
        var refused = RefuseUnlessReviewableBy(reviewerId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        var document = LiveDocuments.SingleOrDefault(d => d.DocumentTypeId == documentTypeId);
        if (document is null)
        {
            return RegistrationResult.Fail(RegistrationErrors.DocumentNotFound);
        }

        document.VerificationStatus = verified ? DocumentVerification.Verified : DocumentVerification.Rejected;
        document.VerifiedBy = reviewerId;
        document.VerifiedAt = now;
        document.ReviewerRemarks = remarks;

        Touch(now);
        return RegistrationResult.Ok;
    }

    /// <summary>Records, or changes, the answer to one checklist item, while under review.</summary>
    public RegistrationResult RecordChecklist(short checklistItemId, bool passed, string? remarks, Guid reviewerId, DateTime now)
    {
        var refused = RefuseUnlessReviewableBy(reviewerId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        var entry = ChecklistEntries.SingleOrDefault(e => e.ChecklistItemId == checklistItemId);
        if (entry is null)
        {
            entry = new RegistrationChecklistEntry { RequestId = Id, ChecklistItemId = checklistItemId };
            ChecklistEntries.Add(entry);
        }

        entry.IsPassed = passed;
        entry.Remarks = remarks;
        entry.CheckedBy = reviewerId;
        entry.CheckedAt = now;

        Touch(now);
        return RegistrationResult.Ok;
    }

    /// <summary>UnderReview → InfoRequested, with at least one question.</summary>
    public RegistrationResult RequestInformation(IReadOnlyCollection<string> questions, Guid reviewerId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var refused = RefuseUnlessReviewableBy(reviewerId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        var asked = questions.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).ToList();
        if (asked.Count == 0)
        {
            return RegistrationResult.Fail(RegistrationErrors.QuestionRequired);
        }

        foreach (var question in asked)
        {
            Clarifications.Add(new RegistrationClarification
            {
                RequestId = Id,
                AskedBy = reviewerId,
                Question = question,
                AskedAt = now,
            });
        }

        MoveTo(RegistrationStatus.InfoRequested, reviewerId, now);
        return RegistrationResult.Ok;
    }

    // ------------------------------------------------------------------ the decision

    /// <summary>
    /// Whether <paramref name="deciderId"/> could approve now: under review, not their own, every
    /// mandatory document verified and every mandatory checklist item passed.
    /// </summary>
    public RegistrationResult CheckReadyForApproval(
        IReadOnlyCollection<short> mandatoryDocumentTypeIds,
        IReadOnlyCollection<short> mandatoryChecklistItemIds,
        Guid deciderId)
    {
        ArgumentNullException.ThrowIfNull(mandatoryDocumentTypeIds);
        ArgumentNullException.ThrowIfNull(mandatoryChecklistItemIds);

        var refused = RefuseUnlessReviewableBy(deciderId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        var verified = LiveDocuments
            .Where(d => d.VerificationStatus == DocumentVerification.Verified)
            .Select(d => d.DocumentTypeId)
            .ToHashSet();
        if (!mandatoryDocumentTypeIds.All(verified.Contains))
        {
            return RegistrationResult.Fail(RegistrationErrors.DocumentsNotVerified);
        }

        var passed = ChecklistEntries.Where(e => e.IsPassed).Select(e => e.ChecklistItemId).ToHashSet();
        if (!mandatoryChecklistItemIds.All(passed.Contains))
        {
            return RegistrationResult.Fail(RegistrationErrors.ChecklistIncomplete);
        }

        return RegistrationResult.Ok;
    }

    /// <summary>UnderReview → Approved, pointing at the institution the approval created (plan 02, B6).</summary>
    public RegistrationResult Approve(
        int createdInstitutionId,
        IReadOnlyCollection<short> mandatoryDocumentTypeIds,
        IReadOnlyCollection<short> mandatoryChecklistItemIds,
        Guid deciderId,
        DateTime now)
    {
        var ready = CheckReadyForApproval(mandatoryDocumentTypeIds, mandatoryChecklistItemIds, deciderId);
        if (!ready.Succeeded)
        {
            return ready;
        }

        CreatedInstitutionId = createdInstitutionId;
        Decide(deciderId, now, note: null);

        MoveTo(RegistrationStatus.Approved, deciderId, now);
        return RegistrationResult.Ok;
    }

    /// <summary>UnderReview → Rejected, with a reason from the <c>REJECT_REASON</c> lookup. Final.</summary>
    public RegistrationResult Reject(int rejectReasonLookupId, string? note, Guid deciderId, DateTime now)
    {
        var refused = RefuseUnlessReviewableBy(deciderId);
        if (refused is not null)
        {
            return RegistrationResult.Fail(refused);
        }

        RejectReasonLookupId = rejectReasonLookupId;
        Decide(deciderId, now, note);

        MoveTo(RegistrationStatus.Rejected, deciderId, now, note);
        return RegistrationResult.Ok;
    }

    // ------------------------------------------------------------------ helpers

    private string? RefuseUnlessEditableBy(Guid userId) =>
        userId != ApplicantUserId ? RegistrationErrors.NotApplicant
        : !IsEditableByApplicant ? RegistrationErrors.NotEditable
        : null;

    private string? RefuseUnlessReviewableBy(Guid reviewerId) =>
        reviewerId == ApplicantUserId ? RegistrationErrors.OwnApplication
        : Status != RegistrationStatus.UnderReview ? RegistrationErrors.InvalidTransition
        : null;

    private void Decide(Guid deciderId, DateTime now, string? note)
    {
        DecidedBy = deciderId;
        DecidedAt = now;
        DecisionNote = note;
    }

    private void MoveTo(string status, Guid by, DateTime now, string? comment = null)
    {
        StatusHistory.Add(new RegistrationStatusChange
        {
            RequestId = Id,
            FromStatus = Status,
            ToStatus = status,
            ChangedBy = by,
            ChangedAt = now,
            Comment = comment,
        });

        Status = status;
        Touch(now);
    }

    /// <summary>
    /// Always gives <see cref="UpdatedAt"/> a new value. An unchanged value is no change to EF, so the
    /// row would not be written and its RowVersion not checked - which is exactly what happens when
    /// two requests land in the same millisecond, or a clock repeats. Then it moves one millisecond
    /// on, the column's precision (<c>datetime2(3)</c>).
    /// </summary>
    private void Touch(DateTime now) =>
        UpdatedAt = UpdatedAt is { } previous && now <= previous ? previous.AddMilliseconds(1) : now;
}

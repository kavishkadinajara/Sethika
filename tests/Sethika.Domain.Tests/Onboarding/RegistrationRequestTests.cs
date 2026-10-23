using Sethika.Domain.Onboarding;

namespace Sethika.Domain.Tests.Onboarding;

/// <summary>Plan 02, B3: the application's state machine, transition by transition.</summary>
public class RegistrationRequestTests
{
    private static readonly Guid Applicant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Reviewer = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateTime Start = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Start.AddHours(1);

    private const short Certificate = 1;
    private const short Nic = 2;
    private const short Optional = 9;
    private const short RegNoMatches = 10;
    private const short PhoneAnswered = 20;
    private const short Declaration = 7;
    private const int Institution = 42;
    private const int RejectReason = 5;

    private static readonly short[] MandatoryDocuments = [Certificate, Nic];
    private static readonly short[] MandatoryChecklist = [RegNoMatches, PhoneAnswered];

    // ------------------------------------------------------------------ building an application in any status

    private static RegistrationOfficeBearer Bearer(string nic, bool signatory = true) => new()
    {
        FullName = "Trustee " + nic,
        NicNumber = nic,
        Designation = "Trustee",
        Phone = "+94771234567",
        IsSignatory = signatory,
    };

    private static RegistrationRequestDocument Upload(short type, long fileId = 100) => new() { DocumentTypeId = type, FileId = fileId };

    private static RegistrationRequest Draft()
    {
        var request = RegistrationRequest.StartDraft("REQ-2026-000001", Applicant, Start);
        Assert.True(request.ReplaceOfficeBearers([Bearer("901234567V")], Applicant, Start).Succeeded);
        Assert.True(request.AttachDocument(Upload(Certificate), Applicant, Start).Succeeded);
        Assert.True(request.AttachDocument(Upload(Nic), Applicant, Start).Succeeded);
        return request;
    }

    private static RegistrationRequest Submitted()
    {
        var request = Draft();
        Assert.True(request.Submit(MandatoryDocuments, Declaration, "203.0.113.5", Applicant, Start).Succeeded);
        return request;
    }

    private static RegistrationRequest UnderReview()
    {
        var request = Submitted();
        Assert.True(request.StartReview(Reviewer, Start).Succeeded);
        return request;
    }

    private static RegistrationRequest ReadyToApprove()
    {
        var request = UnderReview();
        foreach (var type in MandatoryDocuments)
        {
            Assert.True(request.VerifyDocument(type, verified: true, null, Reviewer, Start).Succeeded);
        }

        foreach (var item in MandatoryChecklist)
        {
            Assert.True(request.RecordChecklist(item, passed: true, null, Reviewer, Start).Succeeded);
        }

        return request;
    }

    private static RegistrationRequest InStatus(string status) => status switch
    {
        RegistrationStatus.Draft => Draft(),
        RegistrationStatus.Submitted => Submitted(),
        RegistrationStatus.UnderReview => ReadyToApprove(),
        RegistrationStatus.InfoRequested => Do(ReadyToApprove(), r => r.RequestInformation(["Which temple society?"], Reviewer, Start)),
        RegistrationStatus.Approved => Do(ReadyToApprove(), r => r.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Start)),
        RegistrationStatus.Rejected => Do(ReadyToApprove(), r => r.Reject(RejectReason, "Duplicate", Reviewer, Start)),
        RegistrationStatus.Withdrawn => Do(Draft(), r => r.Withdraw(Applicant, Start)),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static RegistrationRequest Do(RegistrationRequest request, Func<RegistrationRequest, RegistrationResult> step)
    {
        Assert.True(step(request).Succeeded);
        return request;
    }

    // ------------------------------------------------------------------ the transition table

    private static readonly string[] Statuses =
    [
        RegistrationStatus.Draft, RegistrationStatus.Submitted, RegistrationStatus.UnderReview, RegistrationStatus.InfoRequested,
        RegistrationStatus.Approved, RegistrationStatus.Rejected, RegistrationStatus.Withdrawn,
    ];

    /// <summary>Every transition, who makes it, and the one status (or three) it is allowed from.</summary>
    private static readonly Dictionary<string, (Func<RegistrationRequest, RegistrationResult> Act, string To, string[] From)> Transitions = new()
    {
        ["submit"] = (r => r.Submit(MandatoryDocuments, Declaration, null, Applicant, Later), RegistrationStatus.Submitted, [RegistrationStatus.Draft]),
        ["start review"] = (r => r.StartReview(Reviewer, Later), RegistrationStatus.UnderReview, [RegistrationStatus.Submitted]),
        ["ask"] = (r => r.RequestInformation(["Another question"], Reviewer, Later), RegistrationStatus.InfoRequested, [RegistrationStatus.UnderReview]),
        ["answer"] = (r => r.Answer([.. r.OpenQuestions.Select(_ => "An answer")], Applicant, Later), RegistrationStatus.UnderReview, [RegistrationStatus.InfoRequested]),
        ["approve"] = (r => r.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later), RegistrationStatus.Approved, [RegistrationStatus.UnderReview]),
        ["reject"] = (r => r.Reject(RejectReason, null, Reviewer, Later), RegistrationStatus.Rejected, [RegistrationStatus.UnderReview]),
        ["withdraw"] = (r => r.Withdraw(Applicant, Later), RegistrationStatus.Withdrawn,
            [RegistrationStatus.Draft, RegistrationStatus.Submitted, RegistrationStatus.InfoRequested]),
    };

    public static TheoryData<string, string> EveryTransitionFromEveryStatus()
    {
        var data = new TheoryData<string, string>();
        foreach (var transition in Transitions.Keys)
        {
            foreach (var status in Statuses)
            {
                data.Add(transition, status);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTransitionFromEveryStatus))]
    public void ATransitionHappensFromItsStatusesOnly_AndIsRecorded(string transition, string from)
    {
        var (act, to, allowedFrom) = Transitions[transition];
        var request = InStatus(from);
        var history = request.StatusHistory.Count;

        var result = act(request);

        if (allowedFrom.Contains(from))
        {
            Assert.True(result.Succeeded, $"{transition} from {from}: {result.Error}");
            Assert.Equal(to, request.Status);

            var change = request.StatusHistory.Last();
            Assert.Equal(history + 1, request.StatusHistory.Count);
            Assert.Equal((from, to, Later), (change.FromStatus, change.ToStatus, change.ChangedAt));
        }
        else
        {
            Assert.Equal(RegistrationErrors.InvalidTransition, result.Error);
            Assert.Equal(from, request.Status);
            Assert.Equal(history, request.StatusHistory.Count);
        }
    }

    [Fact]
    public void ADraftStartsWithItsOwnHistoryRow()
    {
        var request = RegistrationRequest.StartDraft("REQ-2026-000001", Applicant, Start);

        var change = Assert.Single(request.StatusHistory);
        Assert.Equal(RegistrationStatus.Draft, request.Status);
        Assert.Equal((null, RegistrationStatus.Draft, Applicant), (change.FromStatus, change.ToStatus, change.ChangedBy));
    }

    // ------------------------------------------------------------------ who may act

    [Theory]
    [InlineData(RegistrationStatus.Draft, true)]
    [InlineData(RegistrationStatus.Submitted, false)]
    [InlineData(RegistrationStatus.UnderReview, false)]
    [InlineData(RegistrationStatus.InfoRequested, true)]
    [InlineData(RegistrationStatus.Approved, false)]
    [InlineData(RegistrationStatus.Rejected, false)]
    [InlineData(RegistrationStatus.Withdrawn, false)]
    public void TheApplicantEditsOnlyADraftOrAnApplicationWithQuestions(string status, bool editable)
    {
        var expected = editable ? null : RegistrationErrors.NotEditable;

        Assert.Equal(expected, InStatus(status).BeginApplicantEdit(Applicant, Later).Error);
        Assert.Equal(expected, InStatus(status).AttachDocument(Upload(Certificate, 200), Applicant, Later).Error);
        Assert.Equal(expected, InStatus(status).ReplaceOfficeBearers([Bearer("801234567V")], Applicant, Later).Error);
    }

    [Fact]
    public void NobodyButTheApplicantChangesOrMovesTheirApplication()
    {
        Assert.Equal(RegistrationErrors.NotApplicant, Draft().BeginApplicantEdit(Stranger, Later).Error);
        Assert.Equal(RegistrationErrors.NotApplicant, Draft().AttachDocument(Upload(Certificate), Stranger, Later).Error);
        Assert.Equal(RegistrationErrors.NotApplicant, Draft().ReplaceOfficeBearers([Bearer("801234567V")], Stranger, Later).Error);
        Assert.Equal(RegistrationErrors.NotApplicant, Draft().Submit(MandatoryDocuments, Declaration, null, Stranger, Later).Error);
        Assert.Equal(RegistrationErrors.NotApplicant, Draft().Withdraw(Stranger, Later).Error);
        Assert.Equal(RegistrationErrors.NotApplicant,
            InStatus(RegistrationStatus.InfoRequested).Answer(["An answer"], Stranger, Later).Error);
    }

    [Fact]
    public void NobodyReviewsOrDecidesTheirOwnApplication()
    {
        // As CK_RegReq_ReviewerNotApplicant and CK_RegReq_DeciderNotApplicant say, but before the save.
        Assert.Equal(RegistrationErrors.OwnApplication, Submitted().StartReview(Applicant, Later).Error);

        var request = ReadyToApprove();
        Assert.Equal(RegistrationErrors.OwnApplication, request.VerifyDocument(Certificate, true, null, Applicant, Later).Error);
        Assert.Equal(RegistrationErrors.OwnApplication, request.RecordChecklist(RegNoMatches, true, null, Applicant, Later).Error);
        Assert.Equal(RegistrationErrors.OwnApplication, request.RequestInformation(["?"], Applicant, Later).Error);
        Assert.Equal(RegistrationErrors.OwnApplication,
            request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Applicant, Later).Error);
        Assert.Equal(RegistrationErrors.OwnApplication, request.Reject(RejectReason, null, Applicant, Later).Error);
        Assert.Equal(RegistrationStatus.UnderReview, request.Status);
    }

    // ------------------------------------------------------------------ submitting

    [Fact]
    public void SubmittingNeedsEveryMandatoryDocumentButNotTheOptionalOnes()
    {
        var request = RegistrationRequest.StartDraft("REQ-2026-000001", Applicant, Start);
        request.ReplaceOfficeBearers([Bearer("901234567V")], Applicant, Start);
        request.AttachDocument(Upload(Certificate), Applicant, Start);
        request.AttachDocument(Upload(Optional), Applicant, Start);

        Assert.Equal(RegistrationErrors.DocumentsMissing, request.Submit(MandatoryDocuments, Declaration, null, Applicant, Later).Error);

        request.AttachDocument(Upload(Nic), Applicant, Start);
        Assert.True(request.Submit(MandatoryDocuments, Declaration, null, Applicant, Later).Succeeded);
    }

    [Fact]
    public void ASupersededUploadDoesNotCountTowardSubmitting()
    {
        // Documents holds the old row too; only a live one satisfies the requirement.
        var request = RegistrationRequest.StartDraft("REQ-2026-000001", Applicant, Start);
        request.ReplaceOfficeBearers([Bearer("901234567V")], Applicant, Start);
        request.AttachDocument(Upload(Certificate), Applicant, Start);
        request.Documents.Add(new RegistrationRequestDocument { DocumentTypeId = Nic, VerificationStatus = DocumentVerification.Superseded });

        Assert.Equal(RegistrationErrors.DocumentsMissing, request.Submit(MandatoryDocuments, Declaration, null, Applicant, Later).Error);
    }

    [Fact]
    public void SubmittingNeedsAnOfficeBearerAndASignatoryAmongThem()
    {
        var none = RegistrationRequest.StartDraft("REQ-2026-000001", Applicant, Start);
        none.AttachDocument(Upload(Certificate), Applicant, Start);
        none.AttachDocument(Upload(Nic), Applicant, Start);
        Assert.Equal(RegistrationErrors.OfficeBearerRequired, none.Submit(MandatoryDocuments, Declaration, null, Applicant, Later).Error);

        none.ReplaceOfficeBearers([Bearer("901234567V", signatory: false)], Applicant, Start);
        Assert.Equal(RegistrationErrors.SignatoryRequired, none.Submit(MandatoryDocuments, Declaration, null, Applicant, Later).Error);
        Assert.Equal(RegistrationStatus.Draft, none.Status);
    }

    [Fact]
    public void SubmittingRecordsTheDeclarationWhenAndWhere()
    {
        var request = Draft();

        Assert.True(request.Submit(MandatoryDocuments, Declaration, "203.0.113.5", Applicant, Later).Succeeded);

        Assert.Equal((Declaration, Later, "203.0.113.5", Later),
            (request.DeclarationDocumentId, request.DeclarationAcceptedAt, request.DeclarationIp, request.SubmittedAt));
    }

    [Fact]
    public void TheSamePersonCannotBeNamedTwiceAsAnOfficeBearer()
    {
        var request = Draft();

        var result = request.ReplaceOfficeBearers([Bearer("901234567V"), Bearer(" 901234567v ")], Applicant, Later);

        Assert.Equal(RegistrationErrors.OfficeBearerDuplicate, result.Error);
        Assert.Single(request.OfficeBearers);
    }

    // ------------------------------------------------------------------ documents

    [Fact]
    public void AReplacedDocumentSupersedesTheOldOneAndMustBeVerifiedAgain()
    {
        var request = InStatus(RegistrationStatus.InfoRequested);
        var verified = request.LiveDocuments.Single(d => d.DocumentTypeId == Certificate);
        Assert.Equal(DocumentVerification.Verified, verified.VerificationStatus);

        Assert.True(request.AttachDocument(Upload(Certificate, fileId: 300), Applicant, Later).Succeeded);

        Assert.Equal(DocumentVerification.Superseded, verified.VerificationStatus);
        var live = request.LiveDocuments.Single(d => d.DocumentTypeId == Certificate);
        Assert.Equal((300L, DocumentVerification.Pending, (Guid?)null), (live.FileId, live.VerificationStatus, live.VerifiedBy));

        request.Answer(["Here is the clearer copy"], Applicant, Later);
        Assert.Equal(RegistrationErrors.DocumentsNotVerified,
            request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later).Error);
    }

    [Fact]
    public void VerifyingADocumentTypeWithNoUploadIsRefused()
    {
        Assert.Equal(RegistrationErrors.DocumentNotFound, UnderReview().VerifyDocument(Optional, true, null, Reviewer, Later).Error);
    }

    // ------------------------------------------------------------------ questions

    [Fact]
    public void AskingNeedsAQuestion()
    {
        var request = UnderReview();

        Assert.Equal(RegistrationErrors.QuestionRequired, request.RequestInformation(["  ", ""], Reviewer, Later).Error);
        Assert.Equal(RegistrationStatus.UnderReview, request.Status);
        Assert.Empty(request.Clarifications);
    }

    [Theory]
    [InlineData("Only one")]
    [InlineData("First| ")]
    [InlineData("First|Second|Third")]
    public void EveryOpenQuestionMustBeAnswered(string answers)
    {
        var responses = answers.Split('|');
        var request = UnderReview();
        request.RequestInformation(["Who signs for the society?", "Is the hall shared?"], Reviewer, Later);

        Assert.Equal(RegistrationErrors.QuestionsUnanswered, request.Answer(responses, Applicant, Later).Error);
        Assert.Equal(RegistrationStatus.InfoRequested, request.Status);
        Assert.All(request.Clarifications, c => Assert.True(c.IsOpen));
    }

    [Fact]
    public void AnswersGoToTheirQuestionsInOrder_AndASecondRoundAsksOnlyTheNewOnes()
    {
        var request = UnderReview();
        request.RequestInformation(["Who signs?", "Is the hall shared?"], Reviewer, Start);

        Assert.True(request.Answer(["The secretary", "No"], Applicant, Later).Succeeded);
        Assert.Equal(["The secretary", "No"], request.Clarifications.Select(c => c.Response));
        Assert.Empty(request.OpenQuestions);

        request.RequestInformation(["Since when?"], Reviewer, Later);
        Assert.Equal("Since when?", Assert.Single(request.OpenQuestions).Question);
        Assert.True(request.Answer(["1952"], Applicant, Later).Succeeded);
    }

    // ------------------------------------------------------------------ the decision

    [Fact]
    public void ApprovingNeedsEveryMandatoryDocumentVerified()
    {
        var request = UnderReview();
        request.VerifyDocument(Certificate, verified: true, null, Reviewer, Later);
        request.VerifyDocument(Nic, verified: false, "Unreadable", Reviewer, Later);
        foreach (var item in MandatoryChecklist)
        {
            request.RecordChecklist(item, passed: true, null, Reviewer, Later);
        }

        Assert.Equal(RegistrationErrors.DocumentsNotVerified,
            request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later).Error);

        request.VerifyDocument(Nic, verified: true, null, Reviewer, Later);
        Assert.True(request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later).Succeeded);
    }

    [Fact]
    public void ApprovingNeedsEveryMandatoryChecklistItemPassed()
    {
        var request = ReadyToApprove();
        request.RecordChecklist(PhoneAnswered, passed: false, "No answer twice", Reviewer, Later);

        Assert.Equal(RegistrationErrors.ChecklistIncomplete,
            request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later).Error);

        request.RecordChecklist(PhoneAnswered, passed: true, "Answered on the third call", Reviewer, Later);
        Assert.Single(request.ChecklistEntries, e => e.ChecklistItemId == PhoneAnswered);
        Assert.True(request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later).Succeeded);
    }

    [Fact]
    public void AnApprovalRecordsTheDeciderAndTheInstitutionItCreated()
    {
        var request = ReadyToApprove();

        request.Approve(Institution, MandatoryDocuments, MandatoryChecklist, Reviewer, Later);

        Assert.Equal((Reviewer, Later, Institution), (request.DecidedBy, request.DecidedAt, request.CreatedInstitutionId));
    }

    [Fact]
    public void ARejectionRecordsTheReasonTheNoteAndTheDecider()
    {
        var request = UnderReview();

        request.Reject(RejectReason, "Same registration number as REQ-2026-000004", Reviewer, Later);

        Assert.Equal((RejectReason, "Same registration number as REQ-2026-000004", Reviewer, Later),
            (request.RejectReasonLookupId, request.DecisionNote, request.DecidedBy, request.DecidedAt));
        Assert.Null(request.CreatedInstitutionId);
    }

    // ------------------------------------------------------------------ concurrency

    public static TheoryData<string> EveryChange() =>
    [
        "edit", "attach", "office bearers", "verify", "checklist",
    ];

    [Theory]
    [MemberData(nameof(EveryChange))]
    public void EveryChangeTouchesTheApplicationRow(string change)
    {
        // A change to a child row alone would not write the parent, and its RowVersion would go
        // unchecked; UpdatedAt is what makes every change a write of the application itself.
        var request = change is "verify" or "checklist" ? UnderReview() : Draft();

        var result = change switch
        {
            "edit" => request.BeginApplicantEdit(Applicant, Later),
            "attach" => request.AttachDocument(Upload(Optional), Applicant, Later),
            "office bearers" => request.ReplaceOfficeBearers([Bearer("801234567V")], Applicant, Later),
            "verify" => request.VerifyDocument(Certificate, true, null, Reviewer, Later),
            _ => request.RecordChecklist(RegNoMatches, true, null, Reviewer, Later),
        };

        Assert.True(result.Succeeded);
        Assert.Equal(Later, request.UpdatedAt);
    }

    [Fact]
    public void AChangeInTheSameMillisecondStillChangesTheApplicationRow()
    {
        // Changed again at the instant it was last changed: an equal value would be no change.
        var request = UnderReview();
        var loaded = request.UpdatedAt!.Value;

        request.VerifyDocument(Certificate, true, null, Reviewer, loaded);
        Assert.Equal(loaded.AddMilliseconds(1), request.UpdatedAt);

        // A clock that has gone backwards is treated the same way.
        request.VerifyDocument(Nic, true, null, Reviewer, loaded.AddMilliseconds(-5));
        Assert.Equal(loaded.AddMilliseconds(2), request.UpdatedAt);
    }
}

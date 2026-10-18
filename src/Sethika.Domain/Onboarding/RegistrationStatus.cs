namespace Sethika.Domain.Onboarding;

/// <summary>
/// <c>org.RegistrationRequests.Status</c>, as <c>CK_RegReq_Status</c> spells it.
/// </summary>
/// <remarks>
/// <code>
/// Draft ─submit─▶ Submitted ─start review─▶ UnderReview ─approve─▶ Approved
///   │                 │                       │  ▲     └─reject──▶ Rejected
///   │                 │               ask info│  │answer
///   │                 │                       ▼  │
///   │                 │                   InfoRequested
///   └─withdraw─▶ Withdrawn ◀─withdraw─────────┘   (from Draft, Submitted or InfoRequested)
/// </code>
/// </remarks>
public static class RegistrationStatus
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string UnderReview = "UnderReview";
    public const string InfoRequested = "InfoRequested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";
}

/// <summary><c>org.RegistrationRequestDocuments.VerificationStatus</c>.</summary>
public static class DocumentVerification
{
    public const string Pending = "Pending";
    public const string Verified = "Verified";
    public const string Rejected = "Rejected";

    /// <summary>Replaced by a newer upload of the same type; kept so the history shows what was sent.</summary>
    public const string Superseded = "Superseded";
}

/// <summary>Why the aggregate refused a change. Each is also the API's error code.</summary>
public static class RegistrationErrors
{
    /// <summary>Not allowed from the application's current status.</summary>
    public const string InvalidTransition = "invalid_transition";

    /// <summary>The applicant may change the application only while it is a draft or has questions open.</summary>
    public const string NotEditable = "application_not_editable";

    /// <summary>Only the applicant may do this.</summary>
    public const string NotApplicant = "not_applicant";

    /// <summary>A reviewer or decider may never act on their own application.</summary>
    public const string OwnApplication = "own_application";

    public const string DocumentsMissing = "documents_missing";
    public const string OfficeBearerRequired = "office_bearer_required";
    public const string SignatoryRequired = "signatory_required";
    public const string OfficeBearerDuplicate = "office_bearer_duplicate";
    public const string QuestionRequired = "question_required";
    public const string QuestionsUnanswered = "questions_unanswered";
    public const string DocumentNotFound = "document_not_found";
    public const string DocumentsNotVerified = "documents_not_verified";
    public const string ChecklistIncomplete = "checklist_incomplete";
}

/// <summary>The outcome of asking the aggregate for a change: done, or the reason it was refused.</summary>
public readonly record struct RegistrationResult(string? Error)
{
    public bool Succeeded => Error is null;

    public static RegistrationResult Ok { get; }

    public static RegistrationResult Fail(string error) => new(error);
}

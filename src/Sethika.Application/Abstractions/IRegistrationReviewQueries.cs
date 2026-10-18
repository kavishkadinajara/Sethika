namespace Sethika.Application.Abstractions;

/// <summary>One application in the reviewer's queue.</summary>
public sealed record ReviewQueueItem(
    string RequestNo,
    string ProposedName,
    string InstitutionType,
    string District,
    string City,
    string Status,
    DateTime? SubmittedAt,
    DateTime UpdatedAt);

/// <summary>A page of the queue, and how many there are in all.</summary>
public sealed record ReviewQueuePage(IReadOnlyList<ReviewQueueItem> Items, int Total, int Page, int PageSize);

/// <summary>A document the application's institution type provides, uploaded or not.</summary>
public sealed record ReviewRequiredDocument(string Code, string Name, bool IsMandatory);

/// <summary>A checklist item that applies to the application, and the reviewer's answer if there is one.</summary>
public sealed record ReviewChecklistItem(string Code, string Question, bool IsMandatory, bool? IsPassed, string? Remarks, DateTime? CheckedAt);

/// <summary>A reason an application can be rejected with.</summary>
public sealed record RejectReasonOption(string Code, string Name);

/// <summary>Everything the reviewer's detail screen shows, in one read.</summary>
public sealed record ReviewView(
    ApplicationView Application,
    IReadOnlyList<ReviewRequiredDocument> RequiredDocuments,
    IReadOnlyList<ReviewChecklistItem> Checklist,
    IReadOnlyList<RejectReasonOption> RejectReasons);

/// <summary>Dapper reads for the reviewer (plan 02, 5.4). A platform session; drafts are never shown.</summary>
/// <remarks>
/// A draft is the applicant's unfinished work, not yet offered to anyone, so the queue, the detail
/// and the documents leave drafts out although Row-Level Security would show them to the platform.
/// </remarks>
public interface IRegistrationReviewQueries
{
    /// <summary>
    /// Oldest submitted first. <paramref name="status"/> null is the work still to do: Submitted,
    /// UnderReview and InfoRequested.
    /// </summary>
    Task<ReviewQueuePage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ReviewView?> GetAsync(string requestNo, CancellationToken cancellationToken = default);

    /// <summary>The <c>REJECT_REASON</c> lookup value's id, or null for an unknown or inactive code.</summary>
    Task<int?> ResolveRejectReasonAsync(string code, CancellationToken cancellationToken = default);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Onboarding.Applying;
using Sethika.Application.Onboarding.Notifications;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;

namespace Sethika.Application.Onboarding.Review;

/// <summary>A reviewer's answer to one checklist item, by the item's code.</summary>
public sealed record ChecklistAnswer(string? Code, bool Passed, string? Remarks);

/// <summary>Whether a review step went through, and if not why, with the fields at fault.</summary>
public sealed record ReviewResult(string? Error, IReadOnlyList<string> Fields)
{
    /// <summary>No application with that number, or a draft: a reviewer is never shown one.</summary>
    public const string NotFound = ApplicationResult.NotFound;

    public const string Stale = ApplicationResult.Stale;
    public const string InvalidDetails = ApplicationResult.InvalidDetails;
    public const string ChecklistItemUnknown = "checklist_item_unknown";
    public const string RejectReasonUnknown = "reject_reason_unknown";

    /// <summary>A document is refused only with a reason, because the applicant is shown it.</summary>
    public const string RemarksRequired = "remarks_required";

    public static ReviewResult Ok { get; } = new(null, []);

    public bool Succeeded => Error is null;

    internal static ReviewResult Failed(string error, IReadOnlyList<string>? fields = null) => new(error, fields ?? []);
}

/// <summary>
/// The reviewer's steps on an application (plan 02, 5.4): start the review, verify documents,
/// answer the checklist, ask questions, reject. Approval is <see cref="Approval.ApproveRegistrationHandler"/>.
/// </summary>
/// <remarks>
/// Every step loads the application, lets the aggregate decide, and saves under its <c>RowVersion</c>:
/// two reviewers acting at once, one wins and the other is told <c>stale</c>. A rejection also takes
/// the decision locks, so it and an approval of the same application cannot both happen.
/// </remarks>
public sealed class RegistrationReviewHandler(
    ISethikaDbContext db,
    IRegistrationLocks locks,
    IRegistrationReviewQueries queries,
    TimeProvider timeProvider)
{
    /// <summary>More than a reviewer needs at once; bounds the request and the texts it causes.</summary>
    public const int MaxQuestions = 10;

    /// <summary>More than there are checklist items; bounds the request.</summary>
    public const int MaxChecklistAnswers = 50;

    // The columns' own limits: RegistrationRequestDocuments.ReviewerRemarks, RegistrationRequestChecklist.Remarks,
    // RegistrationRequestClarifications.Question and RegistrationRequests.DecisionNote.
    private const int MaxRemarks = 500;
    private const int MaxQuestion = 1000;
    private const int MaxNote = 1000;

    /// <summary>Submitted → UnderReview.</summary>
    public Task<ReviewResult> StartReviewAsync(Guid reviewerId, string requestNo, CancellationToken cancellationToken = default) =>
        ChangeAsync(requestNo, (request, now) => Task.FromResult(request.StartReview(reviewerId, now)), cancellationToken);

    /// <summary>Marks the live upload of a document type verified, or refused with remarks.</summary>
    public Task<ReviewResult> VerifyDocumentAsync(
        Guid reviewerId,
        string requestNo,
        string documentTypeCode,
        bool verified,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentTypeCode);

        var tidy = Tidy(remarks);
        if (tidy?.Length > MaxRemarks)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.InvalidDetails, ["remarks"]));
        }

        if (!verified && tidy is null)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.RemarksRequired));
        }

        var code = documentTypeCode.Trim().ToUpperInvariant();
        return ChangeAsync(requestNo, async (request, now) =>
        {
            var typeId = await db.DocumentTypes
                .Where(t => t.Code == code)
                .Select(t => (short?)t.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return typeId is { } id
                ? request.VerifyDocument(id, verified, tidy, reviewerId, now)
                : RegistrationResult.Fail(RegistrationErrors.DocumentNotFound);
        }, cancellationToken);
    }

    /// <summary>Records answers to checklist items that apply to the application's type. All or none.</summary>
    public Task<ReviewResult> RecordChecklistAsync(
        Guid reviewerId,
        string requestNo,
        IReadOnlyList<ChecklistAnswer>? answers,
        CancellationToken cancellationToken = default)
    {
        if (answers is null || answers.Count == 0 || answers.Count > MaxChecklistAnswers)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.InvalidDetails, ["items"]));
        }

        var invalid = new List<string>();
        var tidy = new List<(string Code, bool Passed, string? Remarks)>(answers.Count);
        for (var i = 0; i < answers.Count; i++)
        {
            var code = answers[i].Code?.Trim().ToUpperInvariant();
            var remarks = Tidy(answers[i].Remarks);

            if (string.IsNullOrEmpty(code) || tidy.Exists(t => t.Code == code))
            {
                invalid.Add($"items[{i}].code");
            }

            if (remarks?.Length > MaxRemarks)
            {
                invalid.Add($"items[{i}].remarks");
            }

            tidy.Add((code ?? string.Empty, answers[i].Passed, remarks));
        }

        if (invalid.Count > 0)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.InvalidDetails, invalid));
        }

        var codes = tidy.Select(t => t.Code).ToList();
        return ChangeAsync(requestNo, async (request, now) =>
        {
            var items = await db.VerificationChecklistItems
                .Where(i => i.IsActive && (i.InstitutionTypeId == null || i.InstitutionTypeId == request.InstitutionTypeId) && codes.Contains(i.Code))
                .ToDictionaryAsync(i => i.Code, i => i.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);

            if (items.Count != codes.Count)
            {
                return RegistrationResult.Fail(ReviewResult.ChecklistItemUnknown);
            }

            foreach (var (code, passed, remarks) in tidy)
            {
                var recorded = request.RecordChecklist(items[code], passed, remarks, reviewerId, now);
                if (!recorded.Succeeded)
                {
                    return recorded;
                }
            }

            return RegistrationResult.Ok;
        }, cancellationToken);
    }

    /// <summary>UnderReview → InfoRequested with the reviewer's questions, and a text to the applicant.</summary>
    public Task<ReviewResult> AskAsync(
        Guid reviewerId,
        string requestNo,
        IReadOnlyList<string>? questions,
        CancellationToken cancellationToken = default)
    {
        var asked = questions ?? [];
        if (asked.Count > MaxQuestions)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.InvalidDetails, ["questions"]));
        }

        var invalid = Enumerable.Range(0, asked.Count)
            .Where(i => Tidy(asked[i])?.Length > MaxQuestion)
            .Select(i => $"questions[{i}]")
            .ToList();
        if (invalid.Count > 0)
        {
            return Task.FromResult(ReviewResult.Failed(ReviewResult.InvalidDetails, invalid));
        }

        return ChangeAsync(requestNo, (request, now) =>
        {
            var result = request.RequestInformation(asked, reviewerId, now);
            if (result.Succeeded)
            {
                db.OutboxMessages.Add(OnboardingOutbox.Message(request, OnboardingSmsHandler.InfoRequestedEvent, now));
            }

            return Task.FromResult(result);
        }, cancellationToken);
    }

    /// <summary>UnderReview → Rejected, with a <c>REJECT_REASON</c> code and an optional note the applicant sees.</summary>
    public async Task<ReviewResult> RejectAsync(
        Guid deciderId,
        string requestNo,
        string? reasonCode,
        string? note,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNo);

        var code = reasonCode?.Trim().ToUpperInvariant();
        var tidyNote = Tidy(note);
        var invalid = new List<string>();
        if (string.IsNullOrEmpty(code))
        {
            invalid.Add("reason");
        }

        if (tidyNote?.Length > MaxNote)
        {
            invalid.Add("note");
        }

        if (invalid.Count > 0)
        {
            return ReviewResult.Failed(ReviewResult.InvalidDetails, invalid);
        }

        if (await queries.ResolveRejectReasonAsync(code!, cancellationToken).ConfigureAwait(false) is not { } reasonId)
        {
            return ReviewResult.Failed(ReviewResult.RejectReasonUnknown);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The same locks as an approval, taken first: the two decisions happen one at a time.
        if (!await locks.ClaimForDecisionAsync(requestNo, cancellationToken).ConfigureAwait(false))
        {
            return ReviewResult.Failed(ReviewResult.NotFound);
        }

        var request = await db.LoadRegistrationAsync(requestNo, cancellationToken).ConfigureAwait(false);
        if (request is null || request.Status == RegistrationStatus.Draft)
        {
            return ReviewResult.Failed(ReviewResult.NotFound);
        }

        var rejected = request.Reject(reasonId, tidyNote, deciderId, now);
        if (!rejected.Succeeded)
        {
            return ReviewResult.Failed(rejected.Error!);
        }

        // Codes only: the note is the applicant's to read, not the log's to keep twice.
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = deciderId,
            Action = "Reject",
            EntityName = "RegistrationRequest",
            EntityId = request.RequestNo,
            OldValues = JsonSerializer.Serialize(new { status = RegistrationStatus.UnderReview }),
            NewValues = JsonSerializer.Serialize(new { status = RegistrationStatus.Rejected, reason = code }),
            IpAddress = clientIp,
            OccurredAt = now,
        });
        db.OutboxMessages.Add(OnboardingOutbox.Message(request, OnboardingSmsHandler.RejectedEvent, now));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ReviewResult.Ok;
    }

    /// <summary>Loads a submitted application, lets <paramref name="change"/> act on it, and saves.</summary>
    private async Task<ReviewResult> ChangeAsync(
        string requestNo,
        Func<RegistrationRequest, DateTime, Task<RegistrationResult>> change,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNo);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var request = await db.LoadRegistrationAsync(requestNo, cancellationToken).ConfigureAwait(false);
        if (request is null || request.Status == RegistrationStatus.Draft)
        {
            return ReviewResult.Failed(ReviewResult.NotFound);
        }

        var result = await change(request, now).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ReviewResult.Failed(result.Error!);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ReviewResult.Failed(ReviewResult.Stale);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ReviewResult.Ok;
    }

    private static string? Tidy(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

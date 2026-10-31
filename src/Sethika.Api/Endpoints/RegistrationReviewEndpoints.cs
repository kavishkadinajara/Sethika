using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Onboarding.Approval;
using Sethika.Application.Onboarding.Review;
using Sethika.Domain.Onboarding;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The platform's review of institution applications (plan 02, 5.4), addressed by request number.
/// </summary>
/// <remarks>
/// VIEW and EDIT are a reviewer's; APPROVE and REJECT are a platform administrator's and need a
/// step-up (<c>RequiresMfa</c>), which the permission check enforces before a handler runs. No one
/// acts on their own application: the handlers answer <c>403 own_application</c>, and the database's
/// CHECKs stand behind them.
/// </remarks>
internal static class RegistrationReviewEndpoints
{
    private const string View = "ONBOARDING.REQUESTS.VIEW";
    private const string Edit = "ONBOARDING.REQUESTS.EDIT";
    private const string Approve = "ONBOARDING.REQUESTS.APPROVE";
    private const string Reject = "ONBOARDING.REQUESTS.REJECT";

    private const int MaxPageSize = 100;

    /// <summary>How many decisions the platform dashboard lists.</summary>
    public const int DecidedLength = 10;

    /// <summary>The statuses a reviewer can filter the queue by. Not Draft: drafts are not offered for review.</summary>
    private static readonly HashSet<string> QueueStatuses = new(StringComparer.Ordinal)
    {
        RegistrationStatus.Submitted,
        RegistrationStatus.UnderReview,
        RegistrationStatus.InfoRequested,
        RegistrationStatus.Approved,
        RegistrationStatus.Rejected,
        RegistrationStatus.Withdrawn,
    };

    /// <summary>Errors the client fixes by changing what it sent; the rest are about the application's state.</summary>
    private static readonly HashSet<string> BadRequest = new(StringComparer.Ordinal)
    {
        ReviewResult.InvalidDetails,
        ReviewResult.ChecklistItemUnknown,
        ReviewResult.RejectReasonUnknown,
        ReviewResult.RemarksRequired,
        RegistrationErrors.QuestionRequired,
    };

    public static IEndpointRouteBuilder MapRegistrationReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var registrations = app.MapGroup("/api/admin/registrations");

        registrations.MapGet("/", ListAsync).WithMetadata(new HasPermissionAttribute(View));

        // The platform dashboard's ledger of decisions (plan 05, R4). A literal segment, so it wins over {requestNo}.
        registrations.MapGet("/decided", async (IRegistrationReviewQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.ListDecidedAsync(DecidedLength, ct)))
            .WithMetadata(new HasPermissionAttribute(View));

        // The names behind the codes the queue and the detail carry (types, districts, documents),
        // under the reviewer's own permission: a reviewer holds none of the applicant's.
        registrations.MapGet("/lookups", OnboardingApplicationEndpoints.GetLookupsAsync).WithMetadata(new HasPermissionAttribute(View));
        registrations.MapGet("/{requestNo}", GetAsync).WithMetadata(new HasPermissionAttribute(View));
        registrations.MapGet("/{requestNo}/documents/{documentType}/file", DownloadAsync).WithMetadata(new HasPermissionAttribute(View));

        registrations.MapPost("/{requestNo}/start-review", StartReviewAsync).WithMetadata(new HasPermissionAttribute(Edit));
        registrations.MapPut("/{requestNo}/documents/{documentType}/verification", VerifyDocumentAsync).WithMetadata(new HasPermissionAttribute(Edit));
        registrations.MapPut("/{requestNo}/checklist", RecordChecklistAsync).WithMetadata(new HasPermissionAttribute(Edit));
        registrations.MapPost("/{requestNo}/questions", AskAsync).WithMetadata(new HasPermissionAttribute(Edit));

        registrations.MapPost("/{requestNo}/approve", ApproveAsync).WithMetadata(new HasPermissionAttribute(Approve));
        registrations.MapPost("/{requestNo}/reject", RejectAsync).WithMetadata(new HasPermissionAttribute(Reject));

        return app;
    }

    // ------------------------------------------------------------------ reads

    private static async Task<IResult> ListAsync(string? status, int? page, int? pageSize, IRegistrationReviewQueries queries, CancellationToken cancellationToken)
    {
        if (status is not null && !QueueStatuses.Contains(status))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_status");
        }

        if (page is < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_page");
        }

        return Results.Ok(await queries.ListAsync(status, page ?? 1, pageSize ?? 20, cancellationToken));
    }

    private static async Task<IResult> GetAsync(string requestNo, IRegistrationReviewQueries queries, CancellationToken cancellationToken) =>
        await queries.GetAsync(requestNo, cancellationToken) is { } view
            ? Results.Ok(view)
            : ApiErrors.Error(StatusCodes.Status404NotFound, ReviewResult.NotFound);

    private static async Task<IResult> DownloadAsync(
        string requestNo,
        string documentType,
        HttpContext httpContext,
        IOnboardingApplicationQueries queries,
        IFileStorage storage,
        CancellationToken cancellationToken)
    {
        var file = await queries.GetLiveDocumentFileAsync(null, requestNo, documentType.Trim().ToUpperInvariant(), cancellationToken);
        if (file is null)
        {
            return ApiErrors.Error(StatusCodes.Status404NotFound, RegistrationErrors.DocumentNotFound);
        }

        return await FileResults.AttachmentAsync(httpContext, storage, file, cancellationToken);
    }

    // ------------------------------------------------------------------ review

    private static async Task<IResult> StartReviewAsync(string requestNo, ICurrentUserContext currentUser, RegistrationReviewHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.StartReviewAsync(currentUser.UserId!.Value, requestNo, cancellationToken));

    private static async Task<IResult> VerifyDocumentAsync(
        string requestNo,
        string documentType,
        [FromBody] VerificationBody body,
        ICurrentUserContext currentUser,
        RegistrationReviewHandler handler,
        CancellationToken cancellationToken) =>
        body.Verified is not { } verified
            ? Refused(new ReviewResult(ReviewResult.InvalidDetails, ["verified"]))
            : Answer(await handler.VerifyDocumentAsync(currentUser.UserId!.Value, requestNo, documentType, verified, body.Remarks, cancellationToken));

    private static async Task<IResult> RecordChecklistAsync(string requestNo, [FromBody] ChecklistBody body, ICurrentUserContext currentUser, RegistrationReviewHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.RecordChecklistAsync(currentUser.UserId!.Value, requestNo, body.Items, cancellationToken));

    private static async Task<IResult> AskAsync(string requestNo, [FromBody] QuestionsBody body, ICurrentUserContext currentUser, RegistrationReviewHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.AskAsync(currentUser.UserId!.Value, requestNo, body.Questions, cancellationToken));

    // ------------------------------------------------------------------ decisions

    private static async Task<IResult> ApproveAsync(string requestNo, HttpContext httpContext, ICurrentUserContext currentUser, ApproveRegistrationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ApproveRegistrationCommand(requestNo, currentUser.UserId!.Value, ClientAddress.Of(httpContext)), cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { institutionCode = result.InstitutionCode, slug = result.Slug })
            : Refused(new ReviewResult(result.Error, []));
    }

    private static async Task<IResult> RejectAsync(string requestNo, [FromBody] RejectBody body, HttpContext httpContext, ICurrentUserContext currentUser, RegistrationReviewHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.RejectAsync(currentUser.UserId!.Value, requestNo, body.Reason, body.Note, ClientAddress.Of(httpContext), cancellationToken));

    // ------------------------------------------------------------------ answers

    private static IResult Answer(ReviewResult result) => result.Succeeded ? Results.NoContent() : Refused(result);

    private static IResult Refused(ReviewResult result)
    {
        var status = result.Error is ReviewResult.NotFound or RegistrationErrors.DocumentNotFound ? StatusCodes.Status404NotFound
            : result.Error == RegistrationErrors.OwnApplication ? StatusCodes.Status403Forbidden
            : BadRequest.Contains(result.Error!) ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

internal sealed record VerificationBody(bool? Verified, string? Remarks);

internal sealed record ChecklistBody(IReadOnlyList<ChecklistAnswer>? Items);

internal sealed record QuestionsBody(IReadOnlyList<string>? Questions);

internal sealed record RejectBody(string? Reason, string? Note);

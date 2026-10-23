using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Files;
using Sethika.Application.Onboarding.Applying;
using Sethika.Domain.Onboarding;

namespace Sethika.Api.Endpoints;

/// <summary>
/// An applicant's own institution applications (plan 02, 5.3), addressed by request number.
/// </summary>
/// <remarks>
/// The applicant's permissions are the DONOR role's, which hold only in a personal session (B1): a
/// session switched into an institution is refused here and the web switches back first. Another
/// person's request number answers 404, exactly like one that does not exist.
/// </remarks>
internal static class OnboardingApplicationEndpoints
{
    private const string View = "ONBOARDING.APPLICATION.VIEW";
    private const string Create = "ONBOARDING.APPLICATION.CREATE";
    private const string Edit = "ONBOARDING.APPLICATION.EDIT";

    /// <summary>The largest document type's limit, with room for the multipart framing.</summary>
    private const long MaxUploadRequestBytes = 8L * 1024 * 1024;

    /// <summary>Errors the client fixes by changing what it sent; the rest are about the application's state.</summary>
    private static readonly HashSet<string> BadRequest = new(StringComparer.Ordinal)
    {
        ApplicationResult.InvalidDetails,
        ApplicationResult.DocumentTypeNotAllowed,
        ApplicationResult.DeclarationRequired,
        UploadedFileRules.Empty,
        UploadedFileRules.TooLarge,
        UploadedFileRules.TypeNotAllowed,
        UploadedFileRules.ContentMismatch,
        RegistrationErrors.QuestionsUnanswered,
        RegistrationErrors.OfficeBearerDuplicate,
    };

    public static IEndpointRouteBuilder MapOnboardingApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var applications = app.MapGroup("/api/onboarding/applications");

        applications.MapGet("/lookups", GetLookupsAsync).WithMetadata(new HasPermissionAttribute(View));
        applications.MapGet("/", ListAsync).WithMetadata(new HasPermissionAttribute(View));
        applications.MapGet("/{requestNo}", GetAsync).WithMetadata(new HasPermissionAttribute(View));

        applications.MapPost("/", CreateAsync)
            .WithMetadata(new HasPermissionAttribute(Create))
            .RateLimitPerUser(OnboardingRateLimits.CreatePerUser);

        applications.MapPut("/{requestNo}", UpdateAsync).WithMetadata(new HasPermissionAttribute(Edit));
        applications.MapPut("/{requestNo}/office-bearers", ReplaceOfficeBearersAsync).WithMetadata(new HasPermissionAttribute(Edit));

        // Bearer-token authenticated, never by cookie, so a cross-site form cannot post here: the
        // antiforgery check minimal APIs add for form-bound endpoints has nothing to protect.
        applications.MapPut("/{requestNo}/documents/{documentType}", UploadAsync)
            .WithMetadata(new HasPermissionAttribute(Edit), new RequestSizeLimitAttribute(MaxUploadRequestBytes))
            .DisableAntiforgery()
            .RateLimitPerUser(OnboardingRateLimits.UploadPerUser);

        applications.MapGet("/{requestNo}/documents/{documentType}/file", DownloadAsync).WithMetadata(new HasPermissionAttribute(View));

        applications.MapPost("/{requestNo}/submit", SubmitAsync).WithMetadata(new HasPermissionAttribute(Edit));
        applications.MapPost("/{requestNo}/answers", AnswerAsync).WithMetadata(new HasPermissionAttribute(Edit));
        applications.MapPost("/{requestNo}/withdraw", WithdrawAsync).WithMetadata(new HasPermissionAttribute(Edit));

        return app;
    }

    // ------------------------------------------------------------------ reads

    /// <summary>The form's lookups in one language. The reviewer's screens read them too (RegistrationReviewEndpoints).</summary>
    internal static async Task<IResult> GetLookupsAsync(string? language, IOnboardingApplicationQueries queries, CancellationToken cancellationToken)
    {
        var resolved = SupportedLanguages.Resolve(language);
        if (resolved is null)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_language");
        }

        return Results.Ok(await queries.GetLookupsAsync(resolved, cancellationToken));
    }

    private static async Task<IResult> ListAsync(ICurrentUserContext currentUser, IOnboardingApplicationQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListMineAsync(currentUser.UserId!.Value, cancellationToken));

    private static async Task<IResult> GetAsync(string requestNo, ICurrentUserContext currentUser, IOnboardingApplicationQueries queries, CancellationToken cancellationToken) =>
        await queries.GetMineAsync(currentUser.UserId!.Value, requestNo, cancellationToken) is { } view
            ? Results.Ok(view)
            : ApiErrors.Error(StatusCodes.Status404NotFound, ApplicationResult.NotFound);

    private static async Task<IResult> DownloadAsync(
        string requestNo,
        string documentType,
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        IOnboardingApplicationQueries queries,
        IFileStorage storage,
        CancellationToken cancellationToken)
    {
        var file = await queries.GetLiveDocumentFileAsync(currentUser.UserId!.Value, requestNo, documentType.Trim().ToUpperInvariant(), cancellationToken);
        if (file is null)
        {
            return ApiErrors.Error(StatusCodes.Status404NotFound, RegistrationErrors.DocumentNotFound);
        }

        return await FileResults.AttachmentAsync(httpContext, storage, file, cancellationToken);
    }

    // ------------------------------------------------------------------ changes

    private static async Task<IResult> CreateAsync([FromBody] ApplicationDetails body, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.CreateAsync(currentUser.UserId!.Value, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { requestNo = result.RequestNo }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateAsync(string requestNo, [FromBody] ApplicationDetails body, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.UpdateDetailsAsync(currentUser.UserId!.Value, requestNo, body, cancellationToken));

    private static async Task<IResult> ReplaceOfficeBearersAsync(string requestNo, [FromBody] OfficeBearersBody body, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.ReplaceOfficeBearersAsync(currentUser.UserId!.Value, requestNo, body.OfficeBearers, cancellationToken));

    private static async Task<IResult> UploadAsync(
        string requestNo,
        string documentType,
        IFormFile? file,
        [FromForm] string? referenceNo,
        [FromForm] string? issuedBy,
        [FromForm] string? issuedDate,
        [FromForm] string? expiryDate,
        ICurrentUserContext currentUser,
        ApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        var invalid = new List<string>();
        var issued = Date(issuedDate, "issuedDate", invalid);
        var expiry = Date(expiryDate, "expiryDate", invalid);
        if (file is null)
        {
            invalid.Add("file");
        }

        if (invalid.Count > 0)
        {
            return Refused(new ApplicationResult(ApplicationResult.InvalidDetails, invalid, null));
        }

        var header = new byte[UploadedFileRules.HeaderLength];
        int headerLength;
        await using (var peek = file!.OpenReadStream())
        {
            headerLength = await peek.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        await using var content = file.OpenReadStream();
        var result = await handler.AttachDocumentAsync(
            currentUser.UserId!.Value,
            requestNo,
            documentType,
            new DocumentUpload(file.FileName, file.Length, header.AsMemory(0, headerLength), content, referenceNo, issuedBy, issued, expiry),
            cancellationToken);

        return Answer(result);
    }

    private static async Task<IResult> SubmitAsync(string requestNo, [FromBody] SubmitBody body, HttpContext httpContext, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.SubmitAsync(currentUser.UserId!.Value, requestNo, body.DeclarationSha256, ClientAddress.Of(httpContext), cancellationToken));

    private static async Task<IResult> AnswerAsync(string requestNo, [FromBody] AnswersBody body, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.AnswerAsync(currentUser.UserId!.Value, requestNo, body.Answers, cancellationToken));

    private static async Task<IResult> WithdrawAsync(string requestNo, ICurrentUserContext currentUser, ApplicationHandler handler, CancellationToken cancellationToken) =>
        Answer(await handler.WithdrawAsync(currentUser.UserId!.Value, requestNo, cancellationToken));

    // ------------------------------------------------------------------ answers

    private static IResult Answer(ApplicationResult result) => result.Succeeded ? Results.NoContent() : Refused(result);

    private static IResult Refused(ApplicationResult result)
    {
        var status = result.Error == ApplicationResult.NotFound ? StatusCodes.Status404NotFound
            : BadRequest.Contains(result.Error!) ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }

    /// <summary><c>yyyy-MM-dd</c>, as an HTML date input sends it; anything else is reported, not guessed.</summary>
    private static DateOnly? Date(string? value, string field, List<string> invalid)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        invalid.Add(field);
        return null;
    }
}

internal sealed record OfficeBearersBody(IReadOnlyList<OfficeBearerDetails>? OfficeBearers);

internal sealed record SubmitBody(string? DeclarationSha256);

internal sealed record AnswersBody(IReadOnlyList<string>? Answers);

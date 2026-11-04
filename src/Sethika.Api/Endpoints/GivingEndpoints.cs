using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Donations;
using Sethika.Domain.Bookings;
using Sethika.Domain.Giving;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Donations and receipts (plan 09, D.1): the institution records gifts and voids them, a receipt is read
/// by its institution and by its donor, and a donor sees their own giving.
/// </summary>
/// <remarks>
/// The institution's endpoints act for the institution in the caller's token, never one named in the
/// request. A receipt is addressed by <c>PublicId</c>; another institution's, or another donor's, answers
/// 404 exactly like one that does not exist. Voiding needs <c>GIVING.DONATIONS.DELETE</c>, which needs a
/// step-up (M6): the permission check asks for it before the handler runs.
/// </remarks>
internal static class GivingEndpoints
{
    private const string DonationsView = "GIVING.DONATIONS.VIEW";
    private const string DonationsCreate = "GIVING.DONATIONS.CREATE";
    private const string DonationsVoid = "GIVING.DONATIONS.DELETE";
    private const string MyGivingView = "MYGIVING.DONATIONS.VIEW";

    /// <summary><c>ReceiptNo</c> is <c>VARCHAR(30)</c>.</summary>
    private const int MaxReceiptNo = 30;

    public static IEndpointRouteBuilder MapGivingEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution/donations");

        institution.MapGet("", ListAsync).WithMetadata(new HasPermissionAttribute(DonationsView));
        institution.MapGet("/methods", MethodsAsync).WithMetadata(new HasPermissionAttribute(DonationsView));
        institution.MapGet("/{publicId:guid}", InstitutionReceiptAsync).WithMetadata(new HasPermissionAttribute(DonationsView));
        institution.MapPost("", RecordAsync).WithMetadata(new HasPermissionAttribute(DonationsCreate));
        institution.MapPost("/{receiptNo}/void", VoidAsync).WithMetadata(new HasPermissionAttribute(DonationsVoid));

        var mine = app.MapGroup("/api/me/donations");

        mine.MapGet("", MineAsync).WithMetadata(new HasPermissionAttribute(MyGivingView));
        mine.MapGet("/{publicId:guid}", DonorReceiptAsync).WithMetadata(new HasPermissionAttribute(MyGivingView));

        return app;
    }

    private static async Task<IResult> ListAsync(string? month, ICurrentUserContext currentUser, IGivingQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        DateOnly first;
        if (string.IsNullOrEmpty(month))
        {
            var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
            first = new DateOnly(today.Year, today.Month, 1);
        }
        else if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_month");
        }

        return Results.Ok(await queries.ListDonationsAsync(institutionId, userId, first, cancellationToken));
    }

    /// <summary>The methods a gift at the counter may be recorded with, in en/si/ta (M20).</summary>
    private static async Task<IResult> MethodsAsync(IGivingQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListCounterMethodsAsync(DonationHandler.CounterMethods, cancellationToken));

    private static async Task<IResult> InstitutionReceiptAsync(Guid publicId, ICurrentUserContext currentUser, IGivingQueries queries, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        return await queries.GetInstitutionReceiptAsync(institutionId, publicId, cancellationToken) is { } receipt
            ? Results.Ok(receipt)
            : ApiErrors.Error(StatusCodes.Status404NotFound, DonationResult.NotFound);
    }

    private static async Task<IResult> RecordAsync([FromBody] DonationInput body, ICurrentUserContext currentUser, DonationHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.RecordAsync(institutionId, userId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId, receiptNo = result.ReceiptNo, version = result.Version }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> VoidAsync(string receiptNo, [FromBody] VoidInput body, ICurrentUserContext currentUser, DonationHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        if (receiptNo.Length > MaxReceiptNo)
        {
            return ApiErrors.Error(StatusCodes.Status404NotFound, DonationResult.NotFound);
        }

        var result = await handler.VoidAsync(institutionId, userId, receiptNo, body, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { publicId = result.PublicId, receiptNo = result.ReceiptNo, version = result.Version })
            : Refused(result);
    }

    private static async Task<IResult> MineAsync(ICurrentUserContext currentUser, IGivingQueries queries, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Results.Ok(await queries.ListMyDonationsAsync(userId, cancellationToken))
            : Results.Unauthorized();

    private static async Task<IResult> DonorReceiptAsync(Guid publicId, ICurrentUserContext currentUser, IGivingQueries queries, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return await queries.GetDonorReceiptAsync(userId, publicId, cancellationToken) is { } receipt
            ? Results.Ok(receipt)
            : ApiErrors.Error(StatusCodes.Status404NotFound, DonationResult.NotFound);
    }

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(DonationResult result)
    {
        var status = result.Error switch
        {
            DonationResult.InvalidDetails => StatusCodes.Status400BadRequest,
            DonationResult.NotFound or DonationResult.DonorNotFound => StatusCodes.Status404NotFound,
            DonationErrors.VoidOwnRecord => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Sponsors;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The institution's donor book (plan 08, K1): the people who give regularly, many of whom never use
/// the app. Every endpoint acts for the institution in the caller's token, never one named in the
/// request.
/// </summary>
/// <remarks>
/// A donor is addressed by <c>PublicId</c>. Another institution's donor answers 404, exactly like one
/// that does not exist.
/// </remarks>
internal static class DonorBookEndpoints
{
    private const string SponsorsView = "BOOKINGS.SPONSORS.VIEW";
    private const string SponsorsCreate = "BOOKINGS.SPONSORS.CREATE";
    private const string SponsorsEdit = "BOOKINGS.SPONSORS.EDIT";

    public static IEndpointRouteBuilder MapDonorBookEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution");

        institution.MapGet("/donors", ListAsync).WithMetadata(new HasPermissionAttribute(SponsorsView));
        institution.MapPost("/donors", CreateAsync).WithMetadata(new HasPermissionAttribute(SponsorsCreate));
        institution.MapPut("/donors/{publicId:guid}", UpdateAsync).WithMetadata(new HasPermissionAttribute(SponsorsEdit));

        return app;
    }

    private static async Task<IResult> ListAsync(ICurrentUserContext currentUser, IDonorBookQueries queries, CancellationToken cancellationToken) =>
        currentUser.InstitutionId is { } institutionId
            ? Results.Ok(await queries.ListDonorsAsync(institutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> CreateAsync([FromBody] DonorInput body, ICurrentUserContext currentUser, DonorBookHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.CreateAsync(institutionId, userId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId, version = result.Version }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateAsync(Guid publicId, [FromBody] DonorInput body, ICurrentUserContext currentUser, DonorBookHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.UpdateAsync(institutionId, userId, publicId, body, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(DonorChangeResult result)
    {
        var status = result.Error switch
        {
            DonorChangeResult.InvalidDetails => StatusCodes.Status400BadRequest,
            DonorChangeResult.DonorNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

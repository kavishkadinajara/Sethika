using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Needs;
using Sethika.Domain.Bookings;
using Sethika.Domain.Giving;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The needs board (plan 09, D.2): an institution's needs and the pledges on them, the public board, and
/// a donor's own pledges.
/// </summary>
/// <remarks>
/// The institution's endpoints act for the institution in the caller's token. Another institution's need
/// or pledge answers 404, as one that does not exist does. The board is anonymous and limited per client
/// address, like search (M12). A donor's pledge and cancel go through <see cref="IOwnPledges"/> (M14):
/// nothing from the request reaches them but the caller's id and the address.
/// </remarks>
internal static class NeedEndpoints
{
    private const string NeedsView = "GIVING.NEEDS.VIEW";
    private const string NeedsCreate = "GIVING.NEEDS.CREATE";
    private const string NeedsEdit = "GIVING.NEEDS.EDIT";
    private const string MyGivingView = "MYGIVING.DONATIONS.VIEW";
    private const string MyGivingCreate = "MYGIVING.DONATIONS.CREATE";
    private const string MyGivingEdit = "MYGIVING.DONATIONS.EDIT";

    public static IEndpointRouteBuilder MapNeedEndpoints(this IEndpointRouteBuilder app)
    {
        var board = app.MapGroup("/api/needs").AllowAnonymous();

        board.MapGet("", BoardAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);
        board.MapGet("/lookups", LookupsAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);

        app.MapPost("/api/needs/{publicId:guid}/pledges", PledgeAsync)
            .WithMetadata(new HasPermissionAttribute(MyGivingCreate))
            .RateLimitPerUser(BookingRateLimits.PledgePerUser);

        var needs = app.MapGroup("/api/institution/needs");

        needs.MapGet("", ListAsync).WithMetadata(new HasPermissionAttribute(NeedsView));
        needs.MapPost("", CreateAsync).WithMetadata(new HasPermissionAttribute(NeedsCreate));
        needs.MapPut("/{publicId:guid}", UpdateAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));
        needs.MapPost("/{publicId:guid}/open", OpenAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));
        needs.MapPost("/{publicId:guid}/close", CloseAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));
        needs.MapPost("/{publicId:guid}/cancel", CancelAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));
        needs.MapGet("/{publicId:guid}/pledges", PledgesAsync).WithMetadata(new HasPermissionAttribute(NeedsView));

        var pledges = app.MapGroup("/api/institution/pledges");

        pledges.MapPost("/{publicId:guid}/receive", ReceiveAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));
        pledges.MapPost("/{publicId:guid}/cancel", CancelPledgeAsync).WithMetadata(new HasPermissionAttribute(NeedsEdit));

        var mine = app.MapGroup("/api/me/pledges");

        mine.MapGet("", MineAsync).WithMetadata(new HasPermissionAttribute(MyGivingView));
        mine.MapPost("/{publicId:guid}/cancel", CancelMineAsync).WithMetadata(new HasPermissionAttribute(MyGivingEdit));

        return app;
    }

    /// <summary><c>?district=CMB&amp;type=TEMPLE&amp;category=STATIONERY</c>, or <c>&amp;institution=slug</c> for one institution's page.</summary>
    private static async Task<IResult> BoardAsync(string? district, string? type, string? category, string? institution, INeedQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var filter = new NeedBoardFilter(Code(district), Code(type), Code(category), Code(institution));
        return Results.Ok(await queries.ListBoardAsync(filter, SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime), cancellationToken));
    }

    private static async Task<IResult> LookupsAsync(INeedQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetLookupsAsync(cancellationToken));

    private static async Task<IResult> PledgeAsync(Guid publicId, [FromBody] PledgeInput body, ICurrentUserContext currentUser, IOwnPledges pledges, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await pledges.PledgeAsync(userId, publicId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> ListAsync(ICurrentUserContext currentUser, INeedQueries queries, CancellationToken cancellationToken) =>
        currentUser.InstitutionId is { } institutionId
            ? Results.Ok(await queries.ListNeedsAsync(institutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> CreateAsync([FromBody] NeedInput body, ICurrentUserContext currentUser, NeedHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.CreateAsync(institutionId, userId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateAsync(Guid publicId, [FromBody] NeedInput body, ICurrentUserContext currentUser, NeedHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        return Answer(await handler.UpdateAsync(institutionId, userId, publicId, body, cancellationToken));
    }

    private static async Task<IResult> OpenAsync(Guid publicId, [FromBody] NeedStepInput body, ICurrentUserContext currentUser, NeedHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        return Answer(await handler.PublishAsync(institutionId, userId, publicId, body, cancellationToken));
    }

    private static async Task<IResult> CloseAsync(Guid publicId, [FromBody] NeedStepInput body, ICurrentUserContext currentUser, NeedHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        return Answer(await handler.CloseAsync(institutionId, userId, publicId, body, cancellationToken));
    }

    private static async Task<IResult> CancelAsync(Guid publicId, [FromBody] NeedStepInput body, ICurrentUserContext currentUser, NeedHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        return Answer(await handler.CancelAsync(institutionId, userId, publicId, body, cancellationToken));
    }

    private static async Task<IResult> PledgesAsync(Guid publicId, ICurrentUserContext currentUser, INeedQueries queries, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        return await queries.ListPledgesAsync(institutionId, publicId, cancellationToken) is { } rows
            ? Results.Ok(rows)
            : ApiErrors.Error(StatusCodes.Status404NotFound, NeedErrors.NotFound);
    }

    private static async Task<IResult> ReceiveAsync(Guid publicId, [FromBody] ReceiveInput body, ICurrentUserContext currentUser, PledgeHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.ReceiveAsync(institutionId, userId, publicId, body, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { publicId = result.PublicId, receiptNo = result.ReceiptNo, receiptPublicId = result.ReceiptPublicId })
            : Refused(result);
    }

    private static async Task<IResult> CancelPledgeAsync(Guid publicId, [FromBody] CancelPledgeInput body, ICurrentUserContext currentUser, PledgeHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        return Answer(await handler.CancelAsync(institutionId, userId, publicId, body, cancellationToken));
    }

    private static async Task<IResult> MineAsync(ICurrentUserContext currentUser, INeedQueries queries, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Results.Ok(await queries.ListMyPledgesAsync(userId, cancellationToken))
            : Results.Unauthorized();

    private static async Task<IResult> CancelMineAsync(Guid publicId, ICurrentUserContext currentUser, IOwnPledges pledges, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Answer(await pledges.CancelAsync(userId, publicId, cancellationToken));
    }

    private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Answer(NeedResult result) =>
        result.Succeeded ? Results.Ok(new { publicId = result.PublicId }) : Refused(result);

    private static IResult Refused(NeedResult result)
    {
        var status = result.Error switch
        {
            NeedResult.InvalidDetails => StatusCodes.Status400BadRequest,
            NeedErrors.NotFound or NeedErrors.PledgeNotFound => StatusCodes.Status404NotFound,
            NeedResult.PhoneRequired => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

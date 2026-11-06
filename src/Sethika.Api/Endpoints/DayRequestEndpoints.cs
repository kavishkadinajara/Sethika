using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Sponsors;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Asking for a day a regular donor holds (plan 08, K6): a donor asks on <c>/i/{slug}</c> and lists their
/// requests; the institution lists its tray and gives each day or declines.
/// </summary>
/// <remarks>
/// A request is addressed by its <c>RequestNo</c>. The institution's endpoints act for the institution in
/// the caller's token; another institution's request answers 404, as one that does not exist.
/// </remarks>
internal static class DayRequestEndpoints
{
    private const string MineView = "MYBOOKINGS.BOOKINGS.VIEW";
    private const string MineCreate = "MYBOOKINGS.BOOKINGS.CREATE";
    private const string CalendarView = "BOOKINGS.CALENDAR.VIEW";
    private const string CalendarApprove = "BOOKINGS.CALENDAR.APPROVE";
    private const string CalendarReject = "BOOKINGS.CALENDAR.REJECT";

    /// <summary>The body of giving a day: whose day it is, when more than one regular donor holds it.</summary>
    internal sealed record GiveBody(Guid? Sponsorship);

    /// <summary>The body of declining: an optional reason the asker reads when they sign in.</summary>
    internal sealed record DeclineBody(string? Reason);

    public static IEndpointRouteBuilder MapDayRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/institutions/{slug}/day-requests", AskAsync)
            .WithMetadata(new HasPermissionAttribute(MineCreate))
            .RateLimitPerUser(BookingRateLimits.CreatePerUser);

        app.MapGet("/api/bookings/day-requests", ListMineAsync).WithMetadata(new HasPermissionAttribute(MineView));

        var tray = app.MapGroup("/api/institution/day-requests");
        tray.MapGet("/", ListAsync).WithMetadata(new HasPermissionAttribute(CalendarView));
        tray.MapPost("/{requestNo}/give", GiveAsync).WithMetadata(new HasPermissionAttribute(CalendarApprove)).WakesOutbox();
        tray.MapPost("/{requestNo}/decline", DeclineAsync).WithMetadata(new HasPermissionAttribute(CalendarReject)).WakesOutbox();

        return app;
    }

    private static async Task<IResult> AskAsync(string slug, [FromBody] DayRequestInput body, ICurrentUserContext currentUser, DayRequestHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.AskAsync(currentUser.UserId!.Value, slug, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { requestNo = result.RequestNo }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> ListMineAsync(ICurrentUserContext currentUser, IDayRequestQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListMineAsync(currentUser.UserId!.Value, cancellationToken));

    private static async Task<IResult> ListAsync([FromQuery] bool? all, ICurrentUserContext currentUser, IDayRequestQueries queries, CancellationToken cancellationToken) =>
        currentUser.InstitutionId is { } institutionId
            ? Results.Ok(await queries.ListForInstitutionAsync(institutionId, all ?? false, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> GiveAsync(string requestNo, [FromBody] GiveBody? body, ICurrentUserContext currentUser, DayRequestHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.GiveAsync(institutionId, userId, requestNo, body?.Sponsorship, cancellationToken);
        return result.Succeeded ? Results.Ok(new { requestNo = result.RequestNo, bookingNo = result.BookingNo }) : Refused(result);
    }

    private static async Task<IResult> DeclineAsync(string requestNo, [FromBody] DeclineBody? body, ICurrentUserContext currentUser, DayRequestHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.DeclineAsync(institutionId, userId, requestNo, body?.Reason, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(DayRequestResult result)
    {
        var status = result.Error switch
        {
            DayRequestResult.InvalidDetails => StatusCodes.Status400BadRequest,
            DayRequestResult.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

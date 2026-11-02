using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Sharing;
using Sethika.Domain.Bookings;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Co-sponsors (plan 07, H16-H19): the donor invites people to share a booking and sends them the
/// link; whoever opens it sees the booking's summary, and joins or declines once signed in.
/// </summary>
/// <remarks>
/// The donor's endpoints and the answers hold My bookings' permissions, which apply only in a
/// personal session (plan 02, B1). The summary is anonymous - the link is the credential, and it shows
/// only what H17 lists - and limited per client address.
/// </remarks>
internal static class SharingEndpoints
{
    private const string MineView = "MYBOOKINGS.BOOKINGS.VIEW";
    private const string MineEdit = "MYBOOKINGS.BOOKINGS.EDIT";

    /// <summary><c>booking.Bookings.BookingNo</c>'s length; anything longer cannot be one.</summary>
    private const int MaxBookingNoLength = 20;

    public static IEndpointRouteBuilder MapSharingEndpoints(this IEndpointRouteBuilder app)
    {
        var bookings = app.MapGroup("/api/bookings");

        bookings.MapGet("/shared", ListJoinedAsync).WithMetadata(new HasPermissionAttribute(MineView));
        bookings.MapGet("/{bookingNo}/contributors", ListInvitationsAsync).WithMetadata(new HasPermissionAttribute(MineView));
        bookings.MapPost("/{bookingNo}/contributors", InviteAsync)
            .WithMetadata(new HasPermissionAttribute(MineEdit))
            .RateLimitPerUser(BookingRateLimits.InvitePerUser);
        bookings.MapDelete("/{bookingNo}/contributors/{publicId:guid}", WithdrawAsync).WithMetadata(new HasPermissionAttribute(MineEdit));

        var share = app.MapGroup("/api/share");

        share.MapGet("/{token}", OpenAsync).AllowAnonymous().RateLimitPerClient(BookingRateLimits.ShareReadPerClient);
        share.MapPost("/{token}/join", (string token, ICurrentUserContext user, ShareHandler handler, CancellationToken ct) =>
                AnswerAsync(token, join: true, user, handler, ct))
            .WithMetadata(new HasPermissionAttribute(MineEdit))
            .RateLimitPerUser(BookingRateLimits.ShareAnswerPerUser);
        share.MapPost("/{token}/decline", (string token, ICurrentUserContext user, ShareHandler handler, CancellationToken ct) =>
                AnswerAsync(token, join: false, user, handler, ct))
            .WithMetadata(new HasPermissionAttribute(MineEdit))
            .RateLimitPerUser(BookingRateLimits.ShareAnswerPerUser);

        return app;
    }

    // ------------------------------------------------------------------ the donor's invitations

    private static async Task<IResult> ListInvitationsAsync(string bookingNo, ICurrentUserContext currentUser, ISharingQueries queries, CancellationToken cancellationToken) =>
        IsBookingNo(bookingNo) && await queries.ListInvitationsAsync(currentUser.UserId!.Value, bookingNo, cancellationToken) is { } invitations
            ? Results.Ok(invitations)
            : ApiErrors.Error(StatusCodes.Status404NotFound, ContributorResult.BookingNotFound);

    private static async Task<IResult> InviteAsync(
        string bookingNo,
        [FromBody] InvitationBody body,
        ICurrentUserContext currentUser,
        ContributorHandler handler,
        CancellationToken cancellationToken)
    {
        if (!IsBookingNo(bookingNo))
        {
            return ApiErrors.Error(StatusCodes.Status404NotFound, ContributorResult.BookingNotFound);
        }

        var result = await handler.InviteAsync(currentUser.UserId!.Value, bookingNo.Trim(), new InviteContributorCommand(body.Name, body.Note), cancellationToken);
        return result.Error switch
        {
            null => Results.Json(new { contributor = result.Contributor, token = result.Token }, statusCode: StatusCodes.Status201Created),
            ContributorResult.InvalidDetails => Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest),
            ContributorResult.BookingNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, result.Error),
            _ => ApiErrors.Error(StatusCodes.Status409Conflict, result.Error),
        };
    }

    private static async Task<IResult> WithdrawAsync(string bookingNo, Guid publicId, ICurrentUserContext currentUser, ContributorHandler handler, CancellationToken cancellationToken)
    {
        if (!IsBookingNo(bookingNo))
        {
            return ApiErrors.Error(StatusCodes.Status404NotFound, ContributorResult.InvitationNotFound);
        }

        return await handler.WithdrawAsync(currentUser.UserId!.Value, bookingNo.Trim(), publicId, cancellationToken) switch
        {
            null => Results.NoContent(),
            ContributorResult.InvitationAnswered => ApiErrors.Error(StatusCodes.Status409Conflict, ContributorResult.InvitationAnswered),
            var error => ApiErrors.Error(StatusCodes.Status404NotFound, error),
        };
    }

    // ------------------------------------------------------------------ the link

    private static async Task<IResult> OpenAsync(string token, ShareHandler handler, CancellationToken cancellationToken) =>
        await handler.OpenAsync(token, cancellationToken) is { } summary
            ? Results.Ok(summary)
            : ApiErrors.Error(StatusCodes.Status404NotFound, ShareErrors.LinkNotFound);

    private static async Task<IResult> AnswerAsync(string token, bool join, ICurrentUserContext currentUser, ShareHandler handler, CancellationToken cancellationToken) =>
        await handler.AnswerAsync(token, currentUser.UserId!.Value, join, cancellationToken) switch
        {
            null => Results.NoContent(),
            ShareErrors.LinkNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, ShareErrors.LinkNotFound),
            var error => ApiErrors.Error(StatusCodes.Status409Conflict, error),
        };

    private static async Task<IResult> ListJoinedAsync(ICurrentUserContext currentUser, ShareHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.ListJoinedAsync(currentUser.UserId!.Value, cancellationToken));

    private static bool IsBookingNo(string bookingNo) => !string.IsNullOrWhiteSpace(bookingNo) && bookingNo.Trim().Length <= MaxBookingNoLength;
}

/// <summary>An invitation: the name the donor knows the person by, and what they bring.</summary>
/// <param name="Name">At most <see cref="BookingContributor.MaxName"/> characters.</param>
/// <param name="Note">"Brings dessert"; at most <see cref="BookingContributor.MaxNote"/>.</param>
internal sealed record InvitationBody(string? Name, string? Note);

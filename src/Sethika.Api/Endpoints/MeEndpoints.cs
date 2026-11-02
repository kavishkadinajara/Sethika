using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Language;
using Sethika.Application.Access.Roles;
using Sethika.Application.Bookings.Places;
using Sethika.Domain.Bookings;

namespace Sethika.Api.Endpoints;

/// <summary>The signed-in user's view of their own account.</summary>
/// <remarks>
/// Authenticated but not permission-gated: everything under <c>/api/me</c> reads or changes only
/// the caller's own row, named by the token, never by the request. A permission to see one's own
/// permissions would be circular, and no role should be needed to choose one's own language.
/// </remarks>
internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet(string.Empty, GetProfileAsync);
        me.MapGet("/permissions", GetPermissionsAsync);
        me.MapGet("/summary", GetSummaryAsync);
        me.MapPut("/language", SetLanguageAsync);

        // Invitations onto an institution's staff, addressed to this account (plan 04, X1-X4).
        me.MapGet("/invitations", GetInvitationsAsync);
        me.MapPost("/invitations/{id:guid}/accept", AcceptInvitationAsync);
        me.MapPost("/invitations/{id:guid}/decline", DeclineInvitationAsync);

        // The places this person keeps, the heart on an institution's page (plan 07, H12).
        me.MapGet("/places", ListPlacesAsync);
        me.MapPut("/places/{slug}", KeepPlaceAsync).RateLimitPerUser(BookingRateLimits.PlacePerUser);
        me.MapDelete("/places/{slug}", LetPlaceGoAsync);

        return app;
    }

    /// <summary>The caller's name and preferred language.</summary>
    /// <remarks>
    /// The web app reads the language once a session starts and opens in it (plan A11: the account's
    /// language comes before the locale cookie), so a donor who chose Sinhala on one device sees
    /// Sinhala on the next. No contact details: the app has no screen that needs them yet.
    /// </remarks>
    private static async Task<IResult> GetProfileAsync(
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        IAccountQueries accounts,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        // A valid token for a deleted account: the same answer as a token that is not valid.
        if (await accounts.GetProfileAsync(userId, cancellationToken) is not { } profile)
        {
            return Results.Unauthorized();
        }

        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new
        {
            fullName = profile.FullName,
            displayName = profile.DisplayName,
            preferredLanguage = profile.PreferredLanguage,
        });
    }

    /// <summary>
    /// The caller's own effective permission codes.
    /// </summary>
    /// <remarks>
    /// The Next.js app reads this once per session to decide what to render; it is a convenience
    /// for the UI, not a security boundary - each endpoint still carries its own [HasPermission].
    /// </remarks>
    private static async Task<IResult> GetPermissionsAsync(
        ICurrentUserContext currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var effective = await permissions
            .GetEffectivePermissionsAsync(userId, currentUser.InstitutionId, cancellationToken);

        return Results.Ok(new
        {
            userId,
            institutionId = currentUser.InstitutionId,
            isPlatform = currentUser.IsPlatform,
            permissions = effective.Codes.OrderBy(c => c, StringComparer.Ordinal),
        });
    }

    /// <summary>The counts beside the sidebar's links (plan 05, R3).</summary>
    /// <remarks>
    /// A count is read only when the caller holds the permission of the screen it counts, and is
    /// <c>null</c> (no badge) otherwise: a count is itself information, so it is guarded like the
    /// screen. Never cached; the sidebar asks again on focus and once a minute.
    /// </remarks>
    private static async Task<IResult> GetSummaryAsync(
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        IPermissionService permissions,
        IShellQueries shell,
        IAccessQueries access,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var held = await permissions.GetEffectivePermissionsAsync(userId, currentUser.InstitutionId, cancellationToken);
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);

        int? bookingsToAnswer = currentUser.InstitutionId is { } institutionId && held.Allows("BOOKINGS.CALENDAR.APPROVE")
            ? await shell.CountBookingsToAnswerAsync(institutionId, today, cancellationToken)
            : null;

        int? registrationsWaiting = held.Allows("ONBOARDING.REQUESTS.VIEW")
            ? await shell.CountRegistrationsWaitingAsync(cancellationToken)
            : null;

        var invitations = (await access.GetMyInvitationsAsync(userId, cancellationToken)).Count;

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { bookingsToAnswer, registrationsWaiting, invitations });
    }

    /// <summary><c>org.Institutions.Slug</c>'s length; anything longer cannot be one.</summary>
    private const int MaxSlugLength = 150;

    /// <summary>The active places the caller keeps, the newest first.</summary>
    private static async Task<IResult> ListPlacesAsync(ICurrentUserContext currentUser, IDonorBookingQueries queries, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Results.Ok(await queries.ListPlacesAsync(userId, cancellationToken))
            : Results.Unauthorized();

    /// <summary>Keeps a place: 204, also when it was kept already; 404 for no active institution by that slug.</summary>
    private static async Task<IResult> KeepPlaceAsync(string slug, ICurrentUserContext currentUser, FavoritePlacesHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return IsSlug(slug) && await handler.AddAsync(userId, slug.Trim(), cancellationToken) is null
            ? Results.NoContent()
            : ApiErrors.Error(StatusCodes.Status404NotFound, FavoritePlacesHandler.NotFound);
    }

    /// <summary>Lets a place go: 204 whether or not it was kept.</summary>
    private static async Task<IResult> LetPlaceGoAsync(string slug, ICurrentUserContext currentUser, FavoritePlacesHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (IsSlug(slug))
        {
            await handler.RemoveAsync(userId, slug.Trim(), cancellationToken);
        }

        return Results.NoContent();
    }

    private static bool IsSlug(string slug) => !string.IsNullOrWhiteSpace(slug) && slug.Trim().Length <= MaxSlugLength;

    /// <summary>The caller's invitations still waiting for an answer.</summary>
    /// <remarks>Their own only: the user is the token's, and RLS shows an invitation to its invitee.</remarks>
    private static async Task<IResult> GetInvitationsAsync(
        ICurrentUserContext currentUser,
        IAccessQueries queries,
        CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Results.Ok(await queries.GetMyInvitationsAsync(userId, cancellationToken))
            : Results.Unauthorized();

    /// <summary>
    /// Accepts an invitation addressed to the caller: the role is theirs at once. Not
    /// permission-gated, like the rest of /api/me: answering one's own invitation is one's own
    /// business, and before accepting the caller holds nothing in that institution to gate it by.
    /// </summary>
    private static async Task<IResult> AcceptInvitationAsync(
        Guid id,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await roles.AcceptInvitationAsync(userId, id, cancellationToken);
        return result.Succeeded ? Results.NoContent() : InstitutionRoleEndpoints.Refused(result);
    }

    /// <summary>Declines an invitation addressed to the caller.</summary>
    private static async Task<IResult> DeclineInvitationAsync(
        Guid id,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await roles.DeclineInvitationAsync(userId, id, cancellationToken);
        return result.Succeeded ? Results.NoContent() : InstitutionRoleEndpoints.Refused(result);
    }

    /// <summary>
    /// Sets the caller's preferred language: 204 whether or not it changed, so the web app can send
    /// it on every switch without reading it first.
    /// </summary>
    /// <remarks>
    /// The user is the token's <c>sub</c>, never a value in the body, so a caller can only ever change
    /// their own row. The language reaches the SMS a step-up sends, which reads it from the row.
    /// </remarks>
    private static async Task<IResult> SetLanguageAsync(
        [FromBody] PreferredLanguageBody body,
        ICurrentUserContext currentUser,
        PreferredLanguageHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return await handler.SetAsync(userId, body.Language, cancellationToken) switch
        {
            PreferredLanguageResult.Saved => Results.NoContent(),
            PreferredLanguageResult.UnsupportedLanguage =>
                ApiErrors.Error(StatusCodes.Status400BadRequest, "unsupported_language"),

            // A valid token for a deleted account: the same answer as a token that is not valid.
            PreferredLanguageResult.UserNotFound => Results.Unauthorized(),
            _ => ApiErrors.Error(StatusCodes.Status409Conflict, "concurrency_conflict"),
        };
    }
}

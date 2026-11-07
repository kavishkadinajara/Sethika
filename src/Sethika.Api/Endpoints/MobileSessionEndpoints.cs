using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Application.Access.Session;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Sessions for the mobile app (plan F3): the same sign-in, signup and session handlers as the web,
/// with the refresh token carried in the request and response bodies instead of a cookie.
/// </summary>
/// <remarks>
/// The web keeps its refresh token in an httpOnly cookie so that script on the page can never read
/// it. A native app has no page script and no reliable cookie jar; it keeps the token in the
/// platform's secure store (Keychain, Android Keystore) and sends it explicitly.
///
/// These routes never read or set a cookie. That is what makes them safe without CSRF protection: a
/// hostile page can make a browser send cookies, but it cannot put a refresh token it does not have
/// into a request body. It also means offering them does not weaken the web session - a body token
/// is only handed out for a password, a valid code or a refresh token the caller already holds.
///
/// Rotation, reuse detection, the A19 grace window and the stamp check are all
/// <see cref="SessionHandler"/>'s, unchanged; so are the answers and their codes.
///
/// One route here needs a bearer token as well: the password change, whose answer is a new session
/// because the change ends every old one (F39).
/// </remarks>
internal static class MobileSessionEndpoints
{
    public const string BasePath = "/api/auth/mobile";

    /// <summary>Marks a route whose session goes in the body; read by <see cref="SessionResponses"/>.</summary>
    private sealed class MobileSessionMarker;

    /// <summary>Whether the request reached one of these routes.</summary>
    public static bool IsMobile(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<MobileSessionMarker>() is not null;

    public static IEndpointRouteBuilder MapMobileSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup(BasePath)
            .AllowAnonymous()
            .WithMetadata(new MobileSessionMarker());

        // Code requests, code checks and password reset start no session, so the app calls the
        // ordinary /api/auth routes for those.
        AuthEndpoints.MapSessionStarters(mobile);

        var session = mobile.MapGroup("/session");

        // The same shared budget as the cookie routes: one rule, one counter per address.
        session.MapPost("/refresh", RefreshAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/switch-institution", SwitchInstitutionAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/logout", LogOutAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/logout-all", LogOutEverywhereAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        // Its own group, because AllowAnonymous on a group outranks RequireAuthorization on a route:
        // under `mobile` the password change would stop asking for a token (F39).
        var signedIn = app.MapGroup(BasePath)
            .WithMetadata(new MobileSessionMarker());

        PasswordEndpoints.MapChange(signedIn, "/password");

        return app;
    }

    private static Task<IResult> RefreshAsync(
        [FromBody] MobileRefreshBody body,
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        SessionEndpoints.RotateAsync(httpContext, body.RefreshToken, switchTo: null, sessions, environment, cancellationToken);

    private static Task<IResult> SwitchInstitutionAsync(
        [FromBody] MobileSwitchInstitutionBody body,
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(body.Institution)
            ? Task.FromResult(ApiErrors.Error(StatusCodes.Status400BadRequest, "institution_required"))
            : SessionEndpoints.RotateAsync(
                httpContext, body.RefreshToken, body.Institution.Trim(), sessions, environment, cancellationToken);

    /// <summary>Ends this device's session. 204 whether or not it was still alive, as on the web.</summary>
    private static async Task<IResult> LogOutAsync(
        [FromBody] MobileRefreshBody body,
        SessionHandler sessions,
        CancellationToken cancellationToken)
    {
        await sessions.LogOutAsync(body.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Ends every session of the token's user, on every device and browser.</summary>
    private static async Task<IResult> LogOutEverywhereAsync(
        [FromBody] MobileRefreshBody body,
        SessionHandler sessions,
        CancellationToken cancellationToken)
    {
        var done = await sessions.LogOutEverywhereAsync(body.RefreshToken, cancellationToken);

        return done
            ? Results.NoContent()
            : ApiErrors.Error(StatusCodes.Status401Unauthorized, "session_expired");
    }
}

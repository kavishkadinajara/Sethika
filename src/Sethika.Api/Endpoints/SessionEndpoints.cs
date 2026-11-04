using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Session;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The endpoints the refresh cookie is sent to: refresh, switch institution, switch to one's own
/// account, log out, log out everywhere. All under <c>/api/auth/session</c>, the cookie's path, so the cookie reaches exactly
/// these and nothing else.
/// </summary>
/// <remarks>
/// Authenticated by the cookie rather than a Bearer header - the access token may already have
/// expired, which is the point of refreshing - so every one is CSRF-protected.
/// </remarks>
internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var session = app.MapGroup(AuthCookies.SessionPath)
            .AllowAnonymous()
            .AddEndpointFilter(CsrfProtection.RequireTokenAsync);

        session.MapPost("/refresh", RefreshAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/switch-institution", SwitchInstitutionAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/switch-personal", SwitchPersonalAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        // Not a guessing control either, but each one hashes a cookie and queries for it with no
        // account behind the request yet, so they share the refresh budget rather than have none.
        session.MapPost("/logout", LogOutAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        session.MapPost("/logout-all", LogOutEverywhereAsync)
            .RateLimitPerClient(AuthRateLimits.RefreshPerClient);

        return app;
    }

    private static Task<IResult> RefreshAsync(
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        RotateAsync(
            httpContext,
            httpContext.Request.Cookies[AuthCookies.RefreshCookieName],
            switchTo: null,
            sessions,
            environment,
            cancellationToken);

    private static Task<IResult> SwitchInstitutionAsync(
        [FromBody] SwitchInstitutionBody body,
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(body.Institution)
            ? Task.FromResult(ApiErrors.Error(StatusCodes.Status400BadRequest, "institution_required"))
            : RotateAsync(
                httpContext,
                httpContext.Request.Cookies[AuthCookies.RefreshCookieName],
                body.Institution.Trim(),
                sessions,
                environment,
                cancellationToken);

    /// <summary>
    /// Leaves the institution for the person's own account (plan 04, P1): the session then holds
    /// the global grants - a donor's bookings, applications - which an institution's session does
    /// not (plan 02, B1). Before this there was no way back short of signing out, and signing in
    /// put anyone with exactly one institution straight back into it.
    /// </summary>
    private static Task<IResult> SwitchPersonalAsync(
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        RotateAsync(
            httpContext,
            new RefreshCommand(
                httpContext.Request.Cookies[AuthCookies.RefreshCookieName],
                SwitchToInstitution: null,
                ClientAddress.Of(httpContext),
                UserAgent(httpContext),
                SwitchToPersonal: true,
                PresentedAccessOf(httpContext)),
            sessions,
            environment,
            cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair, switching institution on the way when
    /// <paramref name="switchTo"/> names one. Shared with <see cref="MobileSessionEndpoints"/>, which
    /// passes the token from the body; everything else - the answers, the codes - is the same.
    /// </summary>
    internal static Task<IResult> RotateAsync(
        HttpContext httpContext,
        string? refreshToken,
        string? switchTo,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        RotateAsync(
            httpContext,
            new RefreshCommand(refreshToken, switchTo, ClientAddress.Of(httpContext), UserAgent(httpContext), PresentedAccess: PresentedAccessOf(httpContext)),
            sessions,
            environment,
            cancellationToken);

    /// <summary>
    /// The access token sent with the refresh, if the JWT handler accepted it (signed by us, not
    /// expired): its step-up moves to the new token (plan 09, M28). These endpoints are anonymous, so
    /// none, or an expired one, is simply no carry.
    /// </summary>
    private static PresentedAccessToken? PresentedAccessOf(HttpContext httpContext)
    {
        var user = httpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        return user is { UserId: { } userId, TokenId: { } tokenId } ? new PresentedAccessToken(userId, tokenId) : null;
    }

    private static async Task<IResult> RotateAsync(
        HttpContext httpContext,
        RefreshCommand command,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var result = await sessions.RefreshAsync(command, cancellationToken);

        switch (result.Failure)
        {
            case SessionFailure.None:
                return SessionResponses.Start(
                    httpContext, result.Tokens!, result.Institutions, result.ActiveInstitution, environment, StatusCodes.Status200OK);

            case SessionFailure.InstitutionNotAvailable:
                // The session itself is fine and untouched; only the switch is refused.
                return ApiErrors.Error(StatusCodes.Status403Forbidden, "institution_not_available");

            case SessionFailure.Superseded:
                // Another tab won the race. Its response carries the successor cookie; clearing the
                // cookies here could land after it and delete the very session that survived. The
                // handler says Superseded only while that session is alive; a late request after it
                // ended falls to the default below and clears the cookies like any other.
                return ApiErrors.Error(StatusCodes.Status409Conflict, "session_superseded");

            default:
                // One answer for every dead session - expired, revoked, replayed, stamp changed - so
                // a replayed copy learns nothing about why. The cookies go, and the client signs in.
                // A mobile client has none to clear; it drops the token from its store itself.
                if (!MobileSessionEndpoints.IsMobile(httpContext))
                {
                    AuthCookies.Clear(httpContext, secure: !environment.IsDevelopment());
                }

                return ApiErrors.Error(StatusCodes.Status401Unauthorized, "session_expired");
        }
    }

    private static async Task<IResult> LogOutAsync(
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        await sessions.LogOutAsync(httpContext.Request.Cookies[AuthCookies.RefreshCookieName], cancellationToken);

        AuthCookies.Clear(httpContext, secure: !environment.IsDevelopment());
        return Results.NoContent();
    }

    private static async Task<IResult> LogOutEverywhereAsync(
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var done = await sessions.LogOutEverywhereAsync(
            httpContext.Request.Cookies[AuthCookies.RefreshCookieName],
            cancellationToken);

        AuthCookies.Clear(httpContext, secure: !environment.IsDevelopment());

        return done
            ? Results.NoContent()
            : ApiErrors.Error(StatusCodes.Status401Unauthorized, "session_expired");
    }

    private static string? UserAgent(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }
}

using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Access.Session;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The endpoints the refresh cookie is sent to: refresh, switch institution, log out, log out
/// everywhere. All under <c>/api/auth/session</c>, the cookie's path, so the cookie reaches exactly
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

        session.MapPost("/logout", LogOutAsync);

        session.MapPost("/logout-all", LogOutEverywhereAsync);

        return app;
    }

    private static Task<IResult> RefreshAsync(
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        RotateAsync(httpContext, switchTo: null, sessions, environment, cancellationToken);

    private static Task<IResult> SwitchInstitutionAsync(
        [FromBody] SwitchInstitutionBody body,
        HttpContext httpContext,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(body.Institution)
            ? Task.FromResult(ApiErrors.Error(StatusCodes.Status400BadRequest, "institution_required"))
            : RotateAsync(httpContext, body.Institution.Trim(), sessions, environment, cancellationToken);

    private static async Task<IResult> RotateAsync(
        HttpContext httpContext,
        string? switchTo,
        SessionHandler sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var result = await sessions.RefreshAsync(
            new RefreshCommand(
                httpContext.Request.Cookies[AuthCookies.RefreshCookieName],
                switchTo,
                ClientAddress.Of(httpContext),
                UserAgent(httpContext)),
            cancellationToken);

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
                // cookies here could land after it and delete the very session that survived.
                return ApiErrors.Error(StatusCodes.Status409Conflict, "session_superseded");

            default:
                // One answer for every dead session - expired, revoked, replayed, stamp changed - so
                // a replayed copy learns nothing about why. The cookies go, and the client signs in.
                AuthCookies.Clear(httpContext, secure: !environment.IsDevelopment());
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

using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.PasswordChange;

namespace Sethika.Api.Endpoints;

/// <summary>Password change: <c>POST /api/me/password</c>.</summary>
/// <remarks>
/// Authenticated but not permission-gated, like <c>/api/me/permissions</c>. It acts only on the
/// caller's own account, named by the token and never by the request, and every signed-in user must
/// be able to change their own password - a permission for it could only ever be held by everyone,
/// and a role that lacked it would leave a compromised password with no way out. The proof it asks
/// for is the current password, checked against the same lockout as sign-in.
///
/// No step-up on top of that. The plan ties step-up to <c>RequiresMfa</c> permissions (A20) and this
/// endpoint has none; the current password already re-authenticates the caller, and requiring an SMS
/// code as well would leave a user who has lost their phone unable to secure their account.
///
/// Rate-limited per user rather than per client: the subject is an account, not an address.
/// </remarks>
internal static class PasswordEndpoints
{
    public static IEndpointRouteBuilder MapPasswordEndpoints(this IEndpointRouteBuilder app)
    {
        MapChange(app, "/api/me/password");

        return app;
    }

    /// <summary>The password change, under the given route.</summary>
    /// <remarks>
    /// Mapped twice - here, and under <c>/api/auth/mobile</c> by <see cref="MobileSessionEndpoints"/> - with
    /// the same handler and the same rule. The rule is keyed by name and subject, not by path, so the
    /// mobile copy spends the same budget and is not a way to double it.
    /// </remarks>
    internal static void MapChange(IEndpointRouteBuilder routes, string pattern) =>
        routes.MapPost(pattern, ChangeAsync)
            .RequireAuthorization()
            .RateLimitBy<ChangePasswordBody>(
                AuthRateLimits.PasswordChangePerUser,
                (_, services) => services.GetRequiredService<ICurrentUserContext>().UserId?.ToString());

    private static async Task<IResult> ChangeAsync(
        [FromBody] ChangePasswordBody body,
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        ChangePasswordHandler passwords,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await passwords.HandleAsync(
            new ChangePasswordCommand(
                userId,
                body.CurrentPassword,
                body.NewPassword,
                currentUser.InstitutionId,
                ClientAddress.Of(httpContext),
                UserAgent(httpContext)),
            cancellationToken);

        return result.Failure switch
        {
            // The calling device keeps a session - the same response, cookies included, as a sign-in -
            // because the one it held was revoked with every other when the stamp rotated.
            ChangePasswordFailure.None => SessionResponses.Start(
                httpContext,
                result.Session!.Tokens!,
                result.Session.Institutions,
                result.Session.ActiveInstitution,
                environment,
                StatusCodes.Status200OK),

            ChangePasswordFailure.AccountNotActive => ApiErrors.Error(StatusCodes.Status403Forbidden, "account_not_active"),
            ChangePasswordFailure.PasswordUnchanged => ApiErrors.Error(StatusCodes.Status400BadRequest, "password_unchanged"),
            ChangePasswordFailure.PasswordRejected => ApiErrors.PasswordRejected(result.PasswordFailures),

            // LockedOut answers exactly as a wrong password does, as at sign-in, so a token holder
            // guessing the password cannot tell when the lockout has stopped counting their guesses.
            // 400 rather than sign-in's 401: the access token is fine, and a 401 would send the
            // client to refresh it and try again.
            _ => ApiErrors.Error(StatusCodes.Status400BadRequest, "password_incorrect"),
        };
    }

    private static string? UserAgent(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }
}

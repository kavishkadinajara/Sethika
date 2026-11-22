using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.StepUp;

namespace Sethika.Api.Endpoints;

/// <summary>
/// MFA step-up (plan 5.3): send a code to the signed-in user's own phone, and check it.
/// </summary>
/// <remarks>
/// Authenticated but not permission-gated, like <c>/api/me/permissions</c>. A step-up is how a user
/// comes to exercise a <c>RequiresMfa</c> permission, so demanding a permission first would be
/// circular, and any signed-in user may prove they still hold their own phone. Both are
/// rate-limited per client on the same budgets as the anonymous OTP endpoints; the per-number send
/// cap and the five-attempt burn in OtpService sit beneath that.
/// </remarks>
internal static class StepUpEndpoints
{
    public static IEndpointRouteBuilder MapStepUpEndpoints(this IEndpointRouteBuilder app)
    {
        var stepUp = app.MapGroup("/api/auth/stepup").RequireAuthorization();

        stepUp.MapPost("/request", RequestAsync)
            .RateLimitPerClient(AuthRateLimits.OtpRequestPerClient);

        stepUp.MapPost("/verify", VerifyAsync)
            .RateLimitPerClient(AuthRateLimits.OtpVerifyPerClient);

        return app;
    }

    private static async Task<IResult> RequestAsync(
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        StepUpHandler stepUp,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await stepUp.RequestAsync(userId, cancellationToken);
        if (result.Send is not { } sent)
        {
            return Refused(result.Failure);
        }

        // The cooldown is read off the outstanding code's own TTL, and a code is deleted once used or
        // burned, so in the cooldown the code already texted still works. The form opens again after a
        // cancel and asks at once; told only "too many requests" it showed an error beside a code that
        // was still good (QA DEF-003). Signed in, the account's own phone: nothing is given away.
        return sent.Outcome switch
        {
            OtpSendOutcome.Sent => Results.Accepted(),
            OtpSendOutcome.CooldownActive =>
                ApiErrors.TooManyRequests(httpContext, sent.RetryAfter ?? TimeSpan.FromMinutes(1), "code_already_sent"),
            OtpSendOutcome.RateLimited =>
                ApiErrors.TooManyRequests(httpContext, sent.RetryAfter ?? TimeSpan.FromMinutes(1)),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    private static async Task<IResult> VerifyAsync(
        [FromBody] StepUpVerifyBody body,
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        StepUpHandler stepUp,
        CancellationToken cancellationToken)
    {
        // Every token this API issues has a jti. One without cannot hold a grant (plan A5).
        if (currentUser.UserId is not { } userId || currentUser.TokenId is not { } tokenId)
        {
            return Results.Unauthorized();
        }

        var result = await stepUp.VerifyAsync(
            new StepUpVerifyCommand(userId, tokenId, body.Code, ClientAddress.Of(httpContext), UserAgent(httpContext)),
            cancellationToken);

        if (result.Outcome is not { } outcome)
        {
            return Refused(result.Failure);
        }

        return outcome switch
        {
            OtpVerifyOutcome.Verified => Results.NoContent(),
            OtpVerifyOutcome.Incorrect => Results.Json(
                new { code = "otp_incorrect", attemptsRemaining = result.AttemptsRemaining },
                statusCode: StatusCodes.Status400BadRequest),
            OtpVerifyOutcome.NotFound => ApiErrors.Error(StatusCodes.Status400BadRequest, "otp_not_found"),
            OtpVerifyOutcome.TooManyAttempts => ApiErrors.Error(StatusCodes.Status400BadRequest, "otp_attempts_exhausted"),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    private static IResult Refused(StepUpFailure failure) => failure switch
    {
        StepUpFailure.NoVerifiedPhone => ApiErrors.Error(StatusCodes.Status409Conflict, "no_verified_phone"),
        _ => ApiErrors.Error(StatusCodes.Status403Forbidden, "account_not_active"),
    };

    private static string? UserAgent(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }
}

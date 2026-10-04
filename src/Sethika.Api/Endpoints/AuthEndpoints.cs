using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.PasswordReset;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using SignInResult = Sethika.Application.Access.SignIn.SignInResult;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The anonymous half of authentication: one-time codes, donor signup, password sign-in and the
/// forgotten-password reset.
/// </summary>
/// <remarks>
/// These are the endpoints CLAUDE.md's every-endpoint-has-a-permission rule cannot apply to - there
/// is no user yet - so each is rate-limited instead, per client address and, where the request names
/// one, per identity. The handlers hold the logic; this file maps outcomes to HTTP and sets cookies.
/// </remarks>
internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        // Signup and reset count against their own budgets. Anything that is not a reset counts as
        // signup, so an unsupported purpose is still counted before it is refused.
        auth.MapPost("/otp/request", RequestOtpAsync)
            .RateLimitPerClient<OtpRequestBody>(AuthRateLimits.OtpRequestPerClient, body => !IsResetPurpose(body.Purpose))
            .RateLimitPerClient<OtpRequestBody>(AuthRateLimits.OtpResetRequestPerClient, body => IsResetPurpose(body.Purpose));

        auth.MapPost("/otp/verify", VerifyOtpAsync)
            .RateLimitPerClient<OtpVerifyBody>(AuthRateLimits.OtpVerifyPerClient, body => !IsResetPurpose(body.Purpose))
            .RateLimitPerClient<OtpVerifyBody>(AuthRateLimits.OtpResetVerifyPerClient, body => IsResetPurpose(body.Purpose))
            .RateLimitBy<OtpVerifyBody>(AuthRateLimits.OtpVerifyPerPhone, (body, services) =>
                IsResetPurpose(body.Purpose) ? null : NormalizedPhone(body.PhoneNumber, services))
            .RateLimitBy<OtpVerifyBody>(AuthRateLimits.OtpResetVerifyPerPhone, (body, services) =>
                IsResetPurpose(body.Purpose) ? NormalizedPhone(body.PhoneNumber, services) : null);

        // Anonymous like the rest of this group - whoever forgot their password cannot hold a
        // permission - and gated by the reset ticket instead, which only a verified code yields.
        auth.MapPost("/password/reset", ResetPasswordAsync)
            .RateLimitPerClient(AuthRateLimits.PasswordResetPerClient);

        auth.MapPost("/signup", SignUpAsync)
            .RateLimitPerClient(AuthRateLimits.SignUpPerClient);

        auth.MapPost("/login", LogInAsync)
            .RateLimitPerClient(AuthRateLimits.LoginPerClient)
            .RateLimitBy<LoginBody>(AuthRateLimits.LoginPerName, (body, services) =>
                LoginName.Normalize(body.Login, services.GetRequiredService<IPhoneNumberNormalizer>()).Value);

        return app;
    }

    /// <summary>Sends a signup or password-reset code.</summary>
    /// <remarks>
    /// Only the signup and reset purposes are accepted here. Step-up codes go to the number already
    /// on an authenticated account (slice 5.3); a public endpoint that sent them to any number would
    /// be both an SMS-bombing tool and a way around that flow.
    ///
    /// A number that already has an account is sent a signup code like any other. Refusing would
    /// tell anyone which numbers are registered; this way only the phone's holder can learn it, by
    /// verifying and then being told at signup. A reset answers the same for every number too, but
    /// only an account that may reset gets the SMS (<see cref="PasswordResetHandler"/>).
    /// </remarks>
    private static async Task<IResult> RequestOtpAsync(
        [FromBody] OtpRequestBody body,
        HttpContext httpContext,
        IPhoneNumberNormalizer phoneNumbers,
        IOtpService otp,
        PasswordResetHandler reset,
        CancellationToken cancellationToken)
    {
        if (ParsePurpose(body.Purpose) is not { } purpose)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "unsupported_purpose");
        }

        if (!phoneNumbers.TryNormalize(body.PhoneNumber, out var phoneNumber))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number");
        }

        var language = SupportedLanguages.Resolve(body.Language);
        if (language is null)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_language");
        }

        // A reset ignores the requested language: the SMS goes in the account's own.
        var sent = purpose == OtpPurpose.Reset
            ? await reset.RequestAsync(phoneNumber, cancellationToken)
            : await otp.SendAsync(phoneNumber, OtpPurpose.Signup, language, cancellationToken);

        return sent.Outcome switch
        {
            OtpSendOutcome.Sent => Results.Accepted(),
            OtpSendOutcome.CooldownActive or OtpSendOutcome.RateLimited =>
                ApiErrors.TooManyRequests(httpContext, sent.RetryAfter ?? TimeSpan.FromMinutes(1)),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    /// <summary>
    /// Checks a signup or reset code and returns the ticket that carries it to the next step (plan
    /// A17). Every answer depends only on the code's state in Redis, which a reset decoy makes the
    /// same for numbers without an account.
    /// </summary>
    private static async Task<IResult> VerifyOtpAsync(
        [FromBody] OtpVerifyBody body,
        IPhoneNumberNormalizer phoneNumbers,
        IOtpService otp,
        PasswordResetHandler reset,
        CancellationToken cancellationToken)
    {
        if (ParsePurpose(body.Purpose) is not { } purpose)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "unsupported_purpose");
        }

        if (!phoneNumbers.TryNormalize(body.PhoneNumber, out var phoneNumber))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number");
        }

        var verified = purpose == OtpPurpose.Reset
            ? await reset.VerifyAsync(phoneNumber, body.Code, cancellationToken)
            : await otp.VerifyAsync(phoneNumber, OtpPurpose.Signup, body.Code ?? string.Empty, cancellationToken);

        return verified.Outcome switch
        {
            OtpVerifyOutcome.Verified => Results.Ok(new { ticket = verified.Ticket }),
            OtpVerifyOutcome.Incorrect => Results.Json(
                new { code = "otp_incorrect", attemptsRemaining = verified.AttemptsRemaining },
                statusCode: StatusCodes.Status400BadRequest),
            OtpVerifyOutcome.NotFound => ApiErrors.Error(StatusCodes.Status400BadRequest, "otp_not_found"),
            OtpVerifyOutcome.TooManyAttempts => ApiErrors.Error(StatusCodes.Status400BadRequest, "otp_attempts_exhausted"),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    /// <summary>Creates the donor account, then signs the donor in.</summary>
    private static async Task<IResult> SignUpAsync(
        [FromBody] SignUpBody body,
        HttpContext httpContext,
        SignUpDonorHandler signUp,
        SignInHandler signIn,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var clientAddress = ClientAddress.Of(httpContext);
        var userAgent = UserAgent(httpContext);

        var created = await signUp.HandleAsync(
            new SignUpDonorCommand(
                body.PhoneNumber,
                body.VerificationTicket,
                body.FullName,
                body.Email,
                body.Password,
                body.PreferredLanguage,
                body.AcceptedDocumentHashes,
                clientAddress,
                userAgent),
            cancellationToken);

        if (!created.Succeeded)
        {
            return SignUpError(created);
        }

        // Through the ordinary sign-in, so the first session has its history row and refresh token
        // exactly like every later one. It cannot fail for a correct, just-set password; if it
        // somehow does, the account still exists and the client is sent to the login screen.
        var session = await signIn.HandleAsync(
            new SignInCommand(body.PhoneNumber, body.Password, clientAddress, userAgent),
            cancellationToken);

        return session.Succeeded
            ? StartSession(httpContext, session, environment, StatusCodes.Status201Created)
            : Results.StatusCode(StatusCodes.Status201Created);
    }

    /// <summary>
    /// Sets a new password with a reset ticket. Answers 204 and starts no session: the user signs in
    /// with the new password, and every session from before the reset has ended.
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] PasswordResetBody body,
        PasswordResetHandler reset,
        CancellationToken cancellationToken)
    {
        var result = await reset.ResetAsync(new ResetPasswordCommand(body.Ticket, body.NewPassword), cancellationToken);

        return result.Failure switch
        {
            ResetPasswordFailure.None => Results.NoContent(),
            ResetPasswordFailure.PasswordRejected => PasswordRejected(result.PasswordFailures),
            ResetPasswordFailure.InvalidTicket => ApiErrors.Error(StatusCodes.Status400BadRequest, "reset_ticket_invalid"),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    private static async Task<IResult> LogInAsync(
        [FromBody] LoginBody body,
        HttpContext httpContext,
        SignInHandler signIn,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var result = await signIn.HandleAsync(
            new SignInCommand(body.Login, body.Password, ClientAddress.Of(httpContext), UserAgent(httpContext)),
            cancellationToken);

        return result.Failure switch
        {
            SignInFailure.None => StartSession(httpContext, result, environment, StatusCodes.Status200OK),
            SignInFailure.NotAllowed => ApiErrors.Error(StatusCodes.Status403Forbidden, "account_not_active"),

            // LockedOut answers exactly as InvalidCredentials does. Only an existing account can be
            // locked, so a different answer would confirm the account to whoever caused the lock.
            _ => ApiErrors.Error(StatusCodes.Status401Unauthorized, "invalid_credentials"),
        };
    }

    private static IResult StartSession(
        HttpContext httpContext,
        SignInResult session,
        IHostEnvironment environment,
        int statusCode) =>
        SessionResponses.Start(
            httpContext, session.Tokens!, session.Institutions, session.ActiveInstitution, environment, statusCode);

    private static IResult SignUpError(SignUpDonorResult result) => result.Failure switch
    {
        SignUpFailure.InvalidPhoneNumber => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number"),
        SignUpFailure.InvalidFullName => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_full_name"),
        SignUpFailure.InvalidEmail => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_email"),
        SignUpFailure.InvalidLanguage => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_language"),
        SignUpFailure.PasswordRejected => PasswordRejected(result.PasswordFailures),
        SignUpFailure.ConsentMissing => ApiErrors.Error(StatusCodes.Status400BadRequest, "consent_missing"),
        SignUpFailure.PhoneNotVerified => ApiErrors.Error(StatusCodes.Status400BadRequest, "phone_not_verified"),
        SignUpFailure.AlreadyRegistered => ApiErrors.Error(StatusCodes.Status409Conflict, "already_registered"),
        _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
    };

    /// <summary>The one shape for a refused password, so signup and reset explain it alike.</summary>
    private static IResult PasswordRejected(PasswordFailure failures) => Results.Json(
        new { code = "password_rejected", failures = Describe(failures) },
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Every rule the password broke, by name, for the client to explain in its language.</summary>
    private static string[] Describe(PasswordFailure failures) =>
        [.. Enum.GetValues<PasswordFailure>()
            .Where(f => f != PasswordFailure.None && failures.HasFlag(f))
            .Select(f => f.ToString())];

    /// <summary>The purposes a client may name here; blank is signup, as before reset existed.</summary>
    private static OtpPurpose? ParsePurpose(string? purpose) =>
        string.IsNullOrWhiteSpace(purpose) || string.Equals(purpose, "signup", StringComparison.OrdinalIgnoreCase)
            ? OtpPurpose.Signup
            : IsResetPurpose(purpose) ? OtpPurpose.Reset : null;

    private static bool IsResetPurpose(string? purpose) =>
        string.Equals(purpose, "reset", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizedPhone(string? phoneNumber, IServiceProvider services) =>
        services.GetRequiredService<IPhoneNumberNormalizer>().TryNormalize(phoneNumber, out var normalized)
            ? normalized
            : null;

    private static string? UserAgent(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }
}

using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using SignInResult = Sethika.Application.Access.SignIn.SignInResult;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The anonymous half of authentication: one-time codes, donor signup and password sign-in.
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

        auth.MapPost("/otp/request", RequestOtpAsync)
            .RateLimitPerClient<OtpRequestBody>(body => IsLoginPurpose(body?.Purpose)
                ? AuthRateLimits.OtpLoginRequestPerClient
                : AuthRateLimits.OtpRequestPerClient);

        auth.MapPost("/otp/verify", VerifyOtpAsync)
            .RateLimitPerClient(AuthRateLimits.OtpVerifyPerClient)
            .RateLimitBy<OtpVerifyBody>(AuthRateLimits.OtpVerifyPerPhone, (body, services) => NormalizedPhone(body.PhoneNumber, services));

        // Anonymous like the rest of this group - signing in is how a caller gets an identity to check
        // a permission against - and limited per number as well as per client, as /otp/verify is.
        auth.MapPost("/otp/sign-in", SignInWithOtpAsync)
            .RateLimitPerClient(AuthRateLimits.OtpSignInPerClient)
            .RateLimitBy<OtpSignInBody>(AuthRateLimits.OtpSignInPerPhone, (body, services) => NormalizedPhone(body.PhoneNumber, services));

        auth.MapPost("/signup", SignUpAsync)
            .RateLimitPerClient(AuthRateLimits.SignUpPerClient);

        auth.MapPost("/login", LogInAsync)
            .RateLimitPerClient(AuthRateLimits.LoginPerClient)
            .RateLimitBy<LoginBody>(AuthRateLimits.LoginPerName, (body, services) =>
                LoginName.Normalize(body.Login, services.GetRequiredService<IPhoneNumberNormalizer>()).Value);

        return app;
    }

    /// <summary>Sends a signup or sign-in code.</summary>
    /// <remarks>
    /// Only the signup and login purposes are accepted here. Step-up codes go to the number already
    /// on an authenticated account (slice 5.3); a public endpoint that sent them to any number would
    /// be both an SMS-bombing tool and a way around that flow.
    ///
    /// A signup code goes to a number that already has an account like to any other. Refusing would
    /// tell anyone which numbers are registered; this way only the phone's holder can learn it, by
    /// verifying and then being told at signup.
    ///
    /// A sign-in code goes only to a number with an account that may sign in, but the answer is the
    /// same for every number, in body, status and time (<see cref="OtpSignInHandler"/>).
    /// </remarks>
    private static async Task<IResult> RequestOtpAsync(
        [FromBody] OtpRequestBody body,
        HttpContext httpContext,
        IPhoneNumberNormalizer phoneNumbers,
        IOtpService otp,
        OtpSignInHandler otpSignIn,
        CancellationToken cancellationToken)
    {
        var isLogin = IsLoginPurpose(body.Purpose);
        if (!isLogin && !IsSignupPurpose(body.Purpose))
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

        var sent = isLogin
            ? await otpSignIn.RequestAsync(phoneNumber, cancellationToken)
            : await otp.SendAsync(phoneNumber, OtpPurpose.Signup, language, cancellationToken);

        return sent.Outcome switch
        {
            OtpSendOutcome.Sent => Results.Accepted(),
            OtpSendOutcome.CooldownActive or OtpSendOutcome.RateLimited =>
                ApiErrors.TooManyRequests(httpContext, sent.RetryAfter ?? TimeSpan.FromMinutes(1)),
            _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
        };
    }

    /// <summary>Checks a signup code and returns the ticket that carries it to signup (plan A17).</summary>
    private static async Task<IResult> VerifyOtpAsync(
        [FromBody] OtpVerifyBody body,
        IPhoneNumberNormalizer phoneNumbers,
        IOtpService otp,
        CancellationToken cancellationToken)
    {
        if (!IsSignupPurpose(body.Purpose))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "unsupported_purpose");
        }

        if (!phoneNumbers.TryNormalize(body.PhoneNumber, out var phoneNumber))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number");
        }

        var verified = await otp.VerifyAsync(phoneNumber, OtpPurpose.Signup, body.Code ?? string.Empty, cancellationToken);

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

    /// <summary>Signs in with a sign-in code, answering success exactly as password login does.</summary>
    /// <remarks>
    /// A wrong, expired or burned code, a number with no account and a locked account are one
    /// answer. A suspended account is told so, as password login tells it - only to someone holding
    /// a valid code for its phone.
    /// </remarks>
    private static async Task<IResult> SignInWithOtpAsync(
        [FromBody] OtpSignInBody body,
        HttpContext httpContext,
        IPhoneNumberNormalizer phoneNumbers,
        OtpSignInHandler otpSignIn,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!phoneNumbers.TryNormalize(body.PhoneNumber, out var phoneNumber))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number");
        }

        var result = await otpSignIn.SignInAsync(
            new OtpSignInCommand(phoneNumber, body.Code, ClientAddress.Of(httpContext), UserAgent(httpContext)),
            cancellationToken);

        return result.Failure switch
        {
            SignInFailure.None => StartSession(httpContext, result, environment, StatusCodes.Status200OK),
            SignInFailure.NotAllowed => ApiErrors.Error(StatusCodes.Status403Forbidden, "account_not_active"),
            SignInFailure.Unavailable => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
            _ => ApiErrors.Error(StatusCodes.Status401Unauthorized, "otp_invalid"),
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
        SignUpFailure.PasswordRejected => Results.Json(
            new { code = "password_rejected", failures = Describe(result.PasswordFailures) },
            statusCode: StatusCodes.Status400BadRequest),
        SignUpFailure.ConsentMissing => ApiErrors.Error(StatusCodes.Status400BadRequest, "consent_missing"),
        SignUpFailure.PhoneNotVerified => ApiErrors.Error(StatusCodes.Status400BadRequest, "phone_not_verified"),
        SignUpFailure.AlreadyRegistered => ApiErrors.Error(StatusCodes.Status409Conflict, "already_registered"),
        _ => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "verification_unavailable"),
    };

    /// <summary>Every rule the password broke, by name, for the client to explain in its language.</summary>
    private static string[] Describe(PasswordFailure failures) =>
        [.. Enum.GetValues<PasswordFailure>()
            .Where(f => f != PasswordFailure.None && failures.HasFlag(f))
            .Select(f => f.ToString())];

    private static bool IsSignupPurpose(string? purpose) =>
        string.IsNullOrWhiteSpace(purpose) || string.Equals(purpose, "signup", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoginPurpose(string? purpose) =>
        string.Equals(purpose, "login", StringComparison.OrdinalIgnoreCase);

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

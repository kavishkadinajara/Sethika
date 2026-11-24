using Sethika.Application.Access;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Error responses as a machine-readable code, not an English sentence.
/// </summary>
/// <remarks>
/// The client turns the code into Sinhala, Tamil or English; an English message from the API would
/// have to be parsed or shown untranslated. The body is also deterministic - no trace id - so two
/// failures that must look identical to a caller, such as an unknown login and a wrong password,
/// are byte-for-byte identical.
/// </remarks>
internal static class ApiErrors
{
    /// <summary><c>{ "code": ... }</c> with <paramref name="statusCode"/>.</summary>
    public static IResult Error(int statusCode, string code) =>
        Results.Json(new ErrorBody(code), statusCode: statusCode);

    /// <summary>429 with <c>Retry-After</c> in whole seconds, rounded up and never zero.</summary>
    /// <remarks><c>Retry-After: 0</c> would invite an immediate retry, which is the thing being refused.</remarks>
    /// <param name="code">
    /// <c>too_many_requests</c>, or a more precise reason a client can act on, such as
    /// <c>code_already_sent</c> for a resend cooldown.
    /// </param>
    public static IResult TooManyRequests(HttpContext httpContext, TimeSpan retryAfter, string code = "too_many_requests")
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Error(StatusCodes.Status429TooManyRequests, code);
    }

    /// <summary>
    /// <c>400 { "code": "password_rejected", "failures": [...] }</c>, naming every rule the password
    /// broke for the client to explain in its language.
    /// </summary>
    /// <remarks>One body for signup and password change, so the client explains both with one mapping.</remarks>
    public static IResult PasswordRejected(PasswordFailure failures) =>
        Results.Json(
            new { code = "password_rejected", failures = Describe(failures) },
            statusCode: StatusCodes.Status400BadRequest);

    private static string[] Describe(PasswordFailure failures) =>
        [.. Enum.GetValues<PasswordFailure>()
            .Where(f => f != PasswordFailure.None && failures.HasFlag(f))
            .Select(f => f.ToString())];

    private sealed record ErrorBody(string Code);
}

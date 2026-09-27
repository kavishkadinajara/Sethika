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
    public static IResult TooManyRequests(HttpContext httpContext, TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Error(StatusCodes.Status429TooManyRequests, "too_many_requests");
    }

    private sealed record ErrorBody(string Code);
}

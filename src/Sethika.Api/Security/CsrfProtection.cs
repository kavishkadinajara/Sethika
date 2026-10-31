using System.Security.Cryptography;
using System.Text;
using Sethika.Api.Endpoints;

namespace Sethika.Api.Security;

/// <summary>
/// Double-submit CSRF protection for the cookie-authenticated session endpoints (plan A2).
/// </summary>
/// <remarks>
/// A browser attaches the refresh cookie to any request to <c>/api/auth/session</c>, including one a
/// hostile page makes. It cannot make that page able to read this origin's cookies, so requiring the
/// CSRF cookie's value in the <c>X-CSRF-Token</c> header is something only this site's own script can
/// do. Every other endpoint authenticates with a Bearer header, which a hostile page cannot attach,
/// so this is the only place it is needed.
/// </remarks>
internal static class CsrfProtection
{
    public const string HeaderName = "X-CSRF-Token";

    /// <summary>Refuses the request with 403 unless header and cookie are present and equal.</summary>
    public static async ValueTask<object?> RequireTokenAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;
        var cookie = request.Cookies[AuthCookies.CsrfCookieName];
        var header = request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header) || !Matches(cookie, header))
        {
            return ApiErrors.Error(StatusCodes.Status403Forbidden, "csrf_failed");
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>Constant-time, so the comparison does not reveal how much of a guess was right.</summary>
    private static bool Matches(string cookie, string header) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header));
}

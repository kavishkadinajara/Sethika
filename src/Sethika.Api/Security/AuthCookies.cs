using System.Buffers.Text;
using System.Security.Cryptography;
using Sethika.Application.Access;

namespace Sethika.Api.Security;

/// <summary>
/// The two cookies a session carries (plan A2): the refresh token, which script can never read, and
/// the CSRF token, which script must read.
/// </summary>
/// <remarks>
/// The refresh cookie is httpOnly and scoped to the refresh path, so it is sent only where it is
/// used and no script - including an injected one - can read it. The refresh endpoint is the one
/// cookie-authenticated, state-changing endpoint, so it is the one CSRF can reach; it requires the
/// CSRF cookie's value echoed in an <c>X-CSRF-Token</c> header, which another site cannot do because
/// it cannot read this origin's cookies. Both are SameSite=Lax, which the same-origin rewrite of plan
/// A3 makes sufficient.
///
/// Secure everywhere but Development, where the API is plain http on localhost.
/// </remarks>
internal static class AuthCookies
{
    public const string RefreshCookieName = "sethika-refresh";
    public const string CsrfCookieName = "sethika-csrf";
    public const string RefreshPath = "/api/auth/refresh";

    /// <summary>Sets both cookies for a newly issued session.</summary>
    public static void Append(HttpContext httpContext, IssuedRefreshToken refresh, bool secure)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(refresh);

        httpContext.Response.Cookies.Append(RefreshCookieName, refresh.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = RefreshPath,
            Expires = refresh.ExpiresAt,
            IsEssential = true,
        });

        httpContext.Response.Cookies.Append(CsrfCookieName, Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = refresh.ExpiresAt,
            IsEssential = true,
        });
    }
}

using System.Buffers.Text;
using System.Security.Cryptography;
using Sethika.Application.Access;

namespace Sethika.Api.Security;

/// <summary>
/// The two cookies a session carries (plan A2): the refresh token, which script can never read, and
/// the CSRF token, which script must read.
/// </summary>
/// <remarks>
/// The refresh cookie is httpOnly and scoped to <see cref="SessionPath"/>, so it is sent only to the
/// session endpoints - refresh, switch institution, log out, log out everywhere - and no script,
/// including an injected one, can read it. Those are the cookie-authenticated endpoints CSRF can
/// reach, so each requires the CSRF cookie's value echoed in an <c>X-CSRF-Token</c> header, which
/// another site cannot do because it cannot read this origin's cookies. Both are SameSite=Lax, which
/// the same-origin rewrite of plan A3 makes sufficient.
///
/// The path is <c>/api/auth/session</c>, not the <c>/api/auth/refresh</c> plan A2 first named: a
/// cookie scoped to the refresh path is never sent to logout, which then has no session to end.
///
/// Secure everywhere but Development, where the API is plain http on localhost.
/// </remarks>
internal static class AuthCookies
{
    public const string RefreshCookieName = "sethika-refresh";
    public const string CsrfCookieName = "sethika-csrf";
    public const string SessionPath = "/api/auth/session";

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
            Path = SessionPath,
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

    /// <summary>Removes both cookies - on logout, and when a session turns out to be dead.</summary>
    public static void Clear(HttpContext httpContext, bool secure)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // A cookie is only removed by a Set-Cookie with the same name and path it was set with.
        httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = SessionPath,
        });

        httpContext.Response.Cookies.Delete(CsrfCookieName, new CookieOptions
        {
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }
}

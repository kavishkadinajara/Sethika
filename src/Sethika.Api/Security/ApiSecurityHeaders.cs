namespace Sethika.Api.Security;

/// <summary>
/// Browser security headers on every API response (hardening 2026-11-08, finding W1).
/// </summary>
/// <remarks>
/// The web app sets its own (web/lib/security-headers.ts), but those do not reach a response the
/// <c>/api</c> rewrite carried from here - seen in the local proxy - and the API's own address is
/// reachable without the web in front. The API serves JSON and downloads, never a page, so its policy
/// can be the strictest there is: load nothing, be framed by nobody, send no referrer, sniff nothing.
/// Set before the rest of the pipeline runs, so a 401, a 403 or a 429 carries them too.
/// </remarks>
internal static class ApiSecurityHeaders
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XFrameOptions = "DENY";
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            return next(context);
        });
}

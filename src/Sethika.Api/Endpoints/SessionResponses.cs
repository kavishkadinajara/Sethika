using Sethika.Api.Security;
using Sethika.Application.Access;

namespace Sethika.Api.Endpoints;

/// <summary>The one way a new session is handed to the client, whether from login, signup or refresh.</summary>
internal static class SessionResponses
{
    /// <summary>Sets the session cookies and returns the access token and institution choices.</summary>
    public static IResult Start(
        HttpContext httpContext,
        IssuedTokens tokens,
        IReadOnlyList<InstitutionAccess> institutions,
        InstitutionAccess? activeInstitution,
        IHostEnvironment environment,
        int statusCode)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(environment);

        AuthCookies.Append(httpContext, tokens.Refresh, secure: !environment.IsDevelopment());

        // The body carries a bearer token, so nothing between here and the browser may keep a copy.
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new SessionResponse(
                tokens.Access.Token,
                tokens.Access.ExpiresAt,
                [.. institutions.Select(i => new InstitutionChoice(i.Slug, i.Name))],
                activeInstitution?.Slug),
            statusCode: statusCode);
    }
}

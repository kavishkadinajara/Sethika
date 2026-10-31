using Sethika.Api.Security;
using Sethika.Application.Access;

namespace Sethika.Api.Endpoints;

/// <summary>The one way a new session is handed to the client, whether from login, signup or refresh.</summary>
internal static class SessionResponses
{
    /// <summary>
    /// Returns the access token and institution choices, with the refresh token in the session
    /// cookies - or, on a <see cref="MobileSessionEndpoints"/> route, in the body and no cookie at all.
    /// </summary>
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

        // The body carries a bearer token, so nothing between here and the client may keep a copy.
        httpContext.Response.Headers.CacheControl = "no-store";

        InstitutionChoice[] choices = [.. institutions.Select(i => new InstitutionChoice(i.Slug, i.Name))];

        if (MobileSessionEndpoints.IsMobile(httpContext))
        {
            return Results.Json(
                new MobileSessionResponse(
                    tokens.Access.Token,
                    tokens.Access.ExpiresAt,
                    tokens.Refresh.Token,
                    tokens.Refresh.ExpiresAt,
                    choices,
                    activeInstitution?.Slug),
                statusCode: statusCode);
        }

        AuthCookies.Append(httpContext, tokens.Refresh, secure: !environment.IsDevelopment());

        return Results.Json(
            new SessionResponse(tokens.Access.Token, tokens.Access.ExpiresAt, choices, activeInstitution?.Slug),
            statusCode: statusCode);
    }
}

namespace Sethika.Application.Access;

/// <summary>A signed access token.</summary>
/// <param name="Token">The compact JWT, sent by the client as a Bearer token.</param>
/// <param name="JwtId">
/// The token's <c>jti</c>. Stored on the refresh token row issued alongside it, and what an MFA
/// step-up grant is bound to, so a refreshed token does not inherit a step-up it never earned.
/// </param>
/// <param name="ExpiresAt">Exactly the token's <c>exp</c>.</param>
public sealed record IssuedAccessToken(string Token, string JwtId, DateTimeOffset ExpiresAt);

/// <summary>A new refresh token and the only form of it that may be stored.</summary>
/// <param name="Token">The plaintext, for the httpOnly cookie. Never stored, never logged.</param>
/// <param name="TokenHash">Lowercase hex SHA-256 of <paramref name="Token"/>, for <c>auth.RefreshTokens.TokenHash</c>.</param>
/// <param name="ExpiresAt">When the refresh token stops being accepted.</param>
public sealed record IssuedRefreshToken(string Token, string TokenHash, DateTimeOffset ExpiresAt);

/// <summary>The pair issued at sign-in, at every refresh, and on switching institution.</summary>
/// <param name="Access">The access token.</param>
/// <param name="Refresh">The refresh token that will be exchanged for the next pair.</param>
public sealed record IssuedTokens(IssuedAccessToken Access, IssuedRefreshToken Refresh);

using Sethika.Application.Access;

namespace Sethika.Application.Abstractions;

/// <summary>
/// Mints the two tokens a signed-in session is made of: a short-lived access token the API checks
/// on every request, and a long-lived refresh token that is exchanged for the next pair.
/// </summary>
/// <remarks>
/// Minting only. Storing the refresh token's hash, rotating it and detecting its reuse belong to
/// the sign-in, refresh and switch-institution flows, because each has to do that inside its own
/// transaction alongside its other writes.
///
/// The service signs whatever subject it is given. Deciding that the user may act for the
/// institution named in it - from <c>auth.UserRoles</c>, never from the request - is the caller's
/// job and has to happen first.
/// </remarks>
public interface ITokenService
{
    /// <summary>Issues a new access token and a new refresh token for <paramref name="subject"/>.</summary>
    /// <param name="subject">Who the tokens are for and which institution they act for.</param>
    IssuedTokens Issue(TokenSubject subject);

    /// <summary>
    /// Hashes a presented refresh token exactly as <see cref="Issue"/> hashed it when it was
    /// minted, so it can be looked up in <c>auth.RefreshTokens</c>.
    /// </summary>
    /// <param name="refreshToken">The token as the client presented it.</param>
    /// <returns>Lowercase hex SHA-256, 64 characters.</returns>
    string HashRefreshToken(string refreshToken);
}

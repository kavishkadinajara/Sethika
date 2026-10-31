using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// What the API's token validation and <see cref="JwtTokenService"/> must agree on: issuer,
/// audience, algorithm and how the signing key is derived from configuration.
/// </summary>
public static class SethikaJwt
{
    public const string Issuer = "https://sethika.com";
    public const string Audience = "sethika-api";

    /// <summary>HMAC-SHA256. One API both issues and validates, so a shared secret is enough.</summary>
    public const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;

    /// <summary>Shortest accepted signing key, in bytes.</summary>
    /// <remarks>
    /// RFC 7518 §3.2 requires an HS256 key of at least the hash output size, 256 bits. A shorter key
    /// is also refused by the token library - but only when the first token is signed, which is the
    /// first user trying to log in rather than the deployment that misconfigured it.
    /// </remarks>
    public const int MinSigningKeyBytes = 32;

    /// <summary>
    /// Builds the signing key from the configured secret, refusing one too short to be safe.
    /// </summary>
    /// <remarks>
    /// Both sides call this, so validation and issuing cannot derive the key differently - an
    /// encoding change on one side only would make every issued token fail validation.
    /// </remarks>
    /// <param name="secret">The value of <c>Jwt:SigningKey</c>.</param>
    public static SymmetricSecurityKey CreateSigningKey(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < MinSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinSigningKeyBytes} bytes of random data. Run: " +
                "dotnet user-secrets set \"Jwt:SigningKey\" \"...\" --project src/Sethika.Api");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}

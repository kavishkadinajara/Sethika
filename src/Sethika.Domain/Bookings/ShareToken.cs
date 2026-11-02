using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Sethika.Domain.Bookings;

/// <summary>
/// The secret in a co-sponsor's link, <c>/share/{token}</c> (plan 07, H16): 32 random bytes as
/// base64url, 43 characters. Only its SHA-256 is stored, as with refresh tokens, so a copy of the
/// database opens no link.
/// </summary>
public static class ShareToken
{
    /// <summary>The length of every token: 32 bytes in base64url, without padding.</summary>
    public const int Length = 43;

    /// <summary>A new token, and the hash it is stored under.</summary>
    public static (string Token, string Hash) New()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    /// <summary>
    /// The hash <paramref name="token"/> is stored under, or <see langword="null"/> when it cannot be a
    /// token: anything but 43 base64url characters is refused before the database is asked.
    /// </summary>
    public static string? HashOf(string? token) =>
        token is { Length: Length } && token.All(IsBase64Url) ? Hash(token) : null;

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool IsBase64Url(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}

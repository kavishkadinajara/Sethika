using System.Security.Cryptography;
using System.Text;

namespace Sethika.Infrastructure.Caching;

/// <summary>Builds the Redis key a rate-limit counter lives under.</summary>
/// <remarks>
/// The subject is hashed, not embedded. Subjects include whatever a caller typed into a login box -
/// attacker-chosen and of any length - so embedding them would let one request create an arbitrarily
/// large key, and would put phone numbers and email addresses in plain sight of anyone running
/// <c>KEYS rl:*</c>. The first 128 bits of SHA-256 bound the key and keep collisions out of reach.
/// </remarks>
public static class RateLimitKey
{
    /// <summary>The counter for <paramref name="subject"/> under the rule named <paramref name="ruleName"/>.</summary>
    public static string For(string ruleName, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentNullException.ThrowIfNull(subject);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(subject));

        return $"rl:{ruleName}:{Convert.ToHexStringLower(digest.AsSpan(0, 16))}";
    }
}

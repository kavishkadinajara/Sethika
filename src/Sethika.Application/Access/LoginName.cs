using Sethika.Application.Abstractions;

namespace Sethika.Application.Access;

/// <summary>What a login box held: an email, a phone number, or neither.</summary>
public enum LoginKind
{
    /// <summary>Neither. Never matches an account.</summary>
    Unrecognised = 0,

    /// <summary>An email address, in <see cref="NormalizedEmail"/> form.</summary>
    Email = 1,

    /// <summary>A phone number, in E.164.</summary>
    Phone = 2,
}

/// <summary>A login reduced to the one form it is looked up, recorded and rate-limited by.</summary>
/// <param name="Value">The canonical form, or <see cref="LoginName.Unrecognised"/>.</param>
/// <param name="Kind">Which column it matches.</param>
public readonly record struct NormalizedLogin(string Value, LoginKind Kind);

/// <summary>
/// The one normalisation of a typed login, shared by sign-in and by the sign-in rate limit.
/// </summary>
/// <remarks>
/// Shared so the two cannot disagree. If the limit keyed <c>0771234567</c> and <c>+94771234567</c>
/// apart while sign-in treated them as one account, an attacker would get one guessing budget per
/// spelling of the same number.
/// </remarks>
public static class LoginName
{
    /// <summary>
    /// Stored and counted in place of input that is neither an email nor a phone number. The usual
    /// cause is a password typed into the login box, which must not reach the history table.
    /// </summary>
    public const string Unrecognised = "(unrecognised)";

    /// <summary>Reduces <paramref name="login"/> to its canonical form.</summary>
    public static NormalizedLogin Normalize(string? login, IPhoneNumberNormalizer phoneNumbers)
    {
        ArgumentNullException.ThrowIfNull(phoneNumbers);

        var trimmed = login?.Trim() ?? string.Empty;

        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            return new NormalizedLogin(NormalizedEmail.From(trimmed), LoginKind.Email);
        }

        return phoneNumbers.TryNormalize(trimmed, out var phoneNumber)
            ? new NormalizedLogin(phoneNumber, LoginKind.Phone)
            : new NormalizedLogin(Unrecognised, LoginKind.Unrecognised);
    }
}

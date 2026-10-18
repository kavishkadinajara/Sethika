using Sethika.Application.Access;

namespace Sethika.Application.Abstractions;

/// <summary>Checks a proposed password against policy before it is hashed and stored.</summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// Checks <paramref name="password"/> against every rule and reports all failures at once.
    /// </summary>
    /// <param name="password">The proposed plaintext password.</param>
    /// <param name="personalData">
    /// Values the password must not contain - the user's phone, email and name. A password
    /// containing the phone number it protects is guessable by anyone who knows the account.
    /// </param>
    PasswordValidationResult Validate(string? password, IEnumerable<string?>? personalData = null);
}

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Applies ASP.NET Core Identity's <see cref="PasswordOptions"/>, plus two rules Identity does not
/// have: a maximum length, and a ban on the password containing the user's own contact details.
/// </summary>
/// <remarks>
/// The options type comes from Identity so the policy values are the framework's and are
/// configurable the usual way; the checking is ours because we do not use
/// <c>UserManager</c>'s validator pipeline.
///
/// The maximum length is a denial-of-service control, not a security one. PBKDF2 hashes whatever
/// it is given, so a megabyte password costs a megabyte of hashing on an endpoint reachable
/// without authenticating. OWASP's guidance is to accept long passphrases but cap them, and 128 is
/// far above any real passphrase.
/// </remarks>
public sealed class SethikaPasswordPolicy : IPasswordPolicy
{
    /// <summary>Longest accepted password. A cap on unauthenticated hashing work, not on strength.</summary>
    public const int MaxLength = 128;

    /// <summary>Shortest run of personal data that will be rejected inside a password.</summary>
    /// <remarks>
    /// Comparing whole values would miss the common case: the account is <c>+94771234567</c> and
    /// the password is <c>Sethika771234567!</c>. Six is short enough to catch a phone fragment and
    /// long enough that a common name substring does not reject reasonable passphrases.
    /// </remarks>
    private const int MinPersonalDataRun = 6;

    private readonly PasswordOptions _options;

    /// <summary>Creates the policy from the configured Identity options.</summary>
    public SethikaPasswordPolicy(IOptions<IdentityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value.Password;
    }

    public PasswordValidationResult Validate(
        string? password,
        IEnumerable<string?>? personalData = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return new PasswordValidationResult(PasswordFailure.TooShort);
        }

        var failures = PasswordFailure.None;

        if (password.Length < _options.RequiredLength)
        {
            failures |= PasswordFailure.TooShort;
        }

        if (password.Length > MaxLength)
        {
            failures |= PasswordFailure.TooLong;
        }

        if (_options.RequireUppercase && !password.Any(char.IsUpper))
        {
            failures |= PasswordFailure.MissingUppercase;
        }

        if (_options.RequireLowercase && !password.Any(char.IsLower))
        {
            failures |= PasswordFailure.MissingLowercase;
        }

        if (_options.RequireDigit && !password.Any(char.IsDigit))
        {
            failures |= PasswordFailure.MissingDigit;
        }

        if (_options.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            failures |= PasswordFailure.MissingNonAlphanumeric;
        }

        if (ContainsPersonalData(password, personalData))
        {
            failures |= PasswordFailure.ContainsPersonalData;
        }

        return new PasswordValidationResult(failures);
    }

    /// <summary>
    /// True when the password contains a run of at least <see cref="MinPersonalDataRun"/>
    /// characters taken from any of the user's own identifying values.
    /// </summary>
    private static bool ContainsPersonalData(string password, IEnumerable<string?>? personalData)
    {
        if (personalData is null)
        {
            return false;
        }

        foreach (var value in personalData)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // Digits only, so "+94 77 123 4567" and "0771234567" both reduce to the same run and a
            // password containing either spelling is caught.
            var digits = new string([.. value.Where(char.IsDigit)]);

            if (digits.Length >= MinPersonalDataRun && ContainsRun(password, digits))
            {
                return true;
            }

            if (value.Length >= MinPersonalDataRun && ContainsRun(password, value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="password"/> contains any substring of <paramref name="value"/> of
    /// length <see cref="MinPersonalDataRun"/> or more, compared case-insensitively.
    /// </summary>
    private static bool ContainsRun(string password, string value)
    {
        for (var start = 0; start + MinPersonalDataRun <= value.Length; start++)
        {
            var run = value.AsSpan(start, MinPersonalDataRun);

            if (password.AsSpan().Contains(run, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

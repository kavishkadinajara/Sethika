namespace Sethika.Application.Access;

/// <summary>Why a proposed password was rejected.</summary>
[Flags]
public enum PasswordFailure
{
    /// <summary>The password is acceptable.</summary>
    None = 0,

    /// <summary>Shorter than the configured minimum.</summary>
    TooShort = 1 << 0,

    /// <summary>Longer than the configured maximum.</summary>
    TooLong = 1 << 1,

    /// <summary>Contains no uppercase letter.</summary>
    MissingUppercase = 1 << 2,

    /// <summary>Contains no lowercase letter.</summary>
    MissingLowercase = 1 << 3,

    /// <summary>Contains no digit.</summary>
    MissingDigit = 1 << 4,

    /// <summary>Contains no character outside letters and digits.</summary>
    MissingNonAlphanumeric = 1 << 5,

    /// <summary>Matches one of the user's recent passwords.</summary>
    RecentlyUsed = 1 << 6,

    /// <summary>Contains the user's own phone number, email or name.</summary>
    ContainsPersonalData = 1 << 7,
}

/// <summary>The result of checking a proposed password against policy.</summary>
/// <param name="Failures">
/// Every rule the password broke, not just the first. Reporting one rule at a time makes a user
/// resubmit repeatedly and learn the policy by trial and error.
/// </param>
public readonly record struct PasswordValidationResult(PasswordFailure Failures)
{
    /// <summary>True when the password broke no rules.</summary>
    public bool IsValid => Failures == PasswordFailure.None;

    /// <summary>An acceptable password.</summary>
    public static PasswordValidationResult Valid { get; } = new(PasswordFailure.None);
}

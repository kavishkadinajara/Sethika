namespace Sethika.Application.Access.SignUp;

/// <summary>Why a signup was refused.</summary>
public enum SignUpFailure
{
    /// <summary>The account was created.</summary>
    None = 0,

    /// <summary>Not a number that can receive a text message.</summary>
    InvalidPhoneNumber = 1,

    /// <summary>Missing, or longer than the column allows.</summary>
    InvalidFullName = 2,

    /// <summary>Present but not a plain email address.</summary>
    InvalidEmail = 3,

    /// <summary>Not one of <c>en</c>, <c>si</c>, <c>ta</c>.</summary>
    InvalidLanguage = 4,

    /// <summary>The password broke policy; see <see cref="SignUpDonorResult.PasswordFailures"/>.</summary>
    PasswordRejected = 5,

    /// <summary>The current Terms of Use and Privacy Policy were not both accepted, as shown.</summary>
    ConsentMissing = 6,

    /// <summary>No valid, unspent verification ticket for this number.</summary>
    PhoneNotVerified = 7,

    /// <summary>
    /// The phone number or email already has an account. Only reported after the ticket is spent, so
    /// only to someone who has just proved they hold the phone.
    /// </summary>
    AlreadyRegistered = 8,

    /// <summary>The store behind verification tickets is unreachable.</summary>
    Unavailable = 9,
}

/// <summary>The outcome of a signup.</summary>
/// <param name="Failure"><see cref="SignUpFailure.None"/> when the account was created.</param>
/// <param name="UserId">The new account, when created.</param>
/// <param name="PasswordFailures">Every rule the password broke, when that was the reason.</param>
public sealed record SignUpDonorResult(SignUpFailure Failure, Guid? UserId, PasswordFailure PasswordFailures)
{
    /// <summary>True when the account was created.</summary>
    public bool Succeeded => Failure == SignUpFailure.None;

    /// <summary>The account was created.</summary>
    public static SignUpDonorResult Success(Guid userId) => new(SignUpFailure.None, userId, PasswordFailure.None);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static SignUpDonorResult Failed(SignUpFailure failure, PasswordFailure passwordFailures = PasswordFailure.None) =>
        new(failure, null, passwordFailures);
}

namespace Sethika.Application.Access.PasswordReset;

/// <summary>Why a password reset was refused.</summary>
public enum ResetPasswordFailure
{
    /// <summary>The password was changed.</summary>
    None = 0,

    /// <summary>
    /// The ticket is unknown, expired or spent, was issued before the account's stamp last changed,
    /// or its account can no longer reset this way. One answer for all of them: the ticket's holder
    /// learns nothing about the account from which one it was.
    /// </summary>
    InvalidTicket = 1,

    /// <summary>The password broke policy; see <see cref="ResetPasswordResult.PasswordFailures"/>. The ticket is not spent.</summary>
    PasswordRejected = 2,

    /// <summary>The ticket store is unreachable.</summary>
    Unavailable = 3,
}

/// <summary>The outcome of a password reset.</summary>
/// <param name="Failure"><see cref="ResetPasswordFailure.None"/> when the password was changed.</param>
/// <param name="PasswordFailures">Every rule the password broke, when that was the reason.</param>
public sealed record ResetPasswordResult(ResetPasswordFailure Failure, PasswordFailure PasswordFailures)
{
    /// <summary>True when the password was changed.</summary>
    public bool Succeeded => Failure == ResetPasswordFailure.None;

    /// <summary>The password was changed and every session ended.</summary>
    public static ResetPasswordResult Success { get; } = new(ResetPasswordFailure.None, PasswordFailure.None);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static ResetPasswordResult Failed(ResetPasswordFailure failure, PasswordFailure passwordFailures = PasswordFailure.None) =>
        new(failure, passwordFailures);
}

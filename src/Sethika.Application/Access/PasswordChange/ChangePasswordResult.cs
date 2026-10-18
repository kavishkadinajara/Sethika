using Sethika.Application.Access.SignIn;

namespace Sethika.Application.Access.PasswordChange;

/// <summary>Why a password change was refused.</summary>
public enum ChangePasswordFailure
{
    /// <summary>Changed.</summary>
    None = 0,

    /// <summary>The current password is wrong. Counted toward the lockout, as at sign-in.</summary>
    PasswordIncorrect = 1,

    /// <summary>
    /// The account is locked. The endpoint must answer exactly as for <see cref="PasswordIncorrect"/>,
    /// as sign-in answers a lock exactly as a wrong password. Kept apart here so the history row says
    /// what happened.
    /// </summary>
    LockedOut = 2,

    /// <summary>The account is suspended, deactivated or deleted.</summary>
    AccountNotActive = 3,

    /// <summary>The new password is the current one.</summary>
    PasswordUnchanged = 4,

    /// <summary>The new password broke policy; see <see cref="ChangePasswordResult.PasswordFailures"/>.</summary>
    PasswordRejected = 5,
}

/// <summary>The outcome of a password change.</summary>
/// <param name="Failure"><see cref="ChangePasswordFailure.None"/> when changed.</param>
/// <param name="PasswordFailures">Every policy rule the new password broke, when it was rejected.</param>
/// <param name="Session">The calling device's new session, when changed.</param>
public sealed record ChangePasswordResult(
    ChangePasswordFailure Failure,
    PasswordFailure PasswordFailures,
    SignInResult? Session)
{
    /// <summary>True when the password was changed.</summary>
    public bool Succeeded => Failure == ChangePasswordFailure.None;

    /// <summary>Changed, and the calling device holds <paramref name="session"/>.</summary>
    public static ChangePasswordResult Success(SignInResult session) =>
        new(ChangePasswordFailure.None, PasswordFailure.None, session);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static ChangePasswordResult Failed(
        ChangePasswordFailure failure,
        PasswordFailure passwordFailures = PasswordFailure.None) =>
        new(failure, passwordFailures, null);
}

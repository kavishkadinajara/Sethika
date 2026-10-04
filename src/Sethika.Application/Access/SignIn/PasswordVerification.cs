namespace Sethika.Application.Access.SignIn;

/// <summary>The outcome of checking a password against an account.</summary>
/// <param name="Failure"><see cref="SignInFailure.None"/> when the password is the account's.</param>
/// <param name="RehashNeeded">
/// True when the stored hash was made under an older, cheaper configuration and should be replaced
/// while the plaintext is in hand.
/// </param>
public readonly record struct PasswordVerification(SignInFailure Failure, bool RehashNeeded)
{
    /// <summary>True when the password is the account's and the account may act.</summary>
    public bool Succeeded => Failure == SignInFailure.None;

    /// <summary>The password is right.</summary>
    public static PasswordVerification Verified(bool rehashNeeded) => new(SignInFailure.None, rehashNeeded);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static PasswordVerification Refused(SignInFailure failure) => new(failure, RehashNeeded: false);
}

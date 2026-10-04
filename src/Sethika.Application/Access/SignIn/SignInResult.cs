namespace Sethika.Application.Access.SignIn;

/// <summary>Why a sign-in was refused.</summary>
/// <remarks>
/// The endpoint must answer <see cref="InvalidCredentials"/> and <see cref="LockedOut"/> identically.
/// Only an existing account can be locked, so a distinct lockout answer tells anyone who fails five
/// times that the phone number or email has an account. They are separate here so the history row
/// records what actually happened.
/// </remarks>
public enum SignInFailure
{
    /// <summary>Signed in.</summary>
    None = 0,

    /// <summary>No such account, or the wrong password. Deliberately one answer.</summary>
    InvalidCredentials = 1,

    /// <summary>Too many recent failures; refused whatever the password.</summary>
    LockedOut = 2,

    /// <summary>
    /// The password was right but the account is suspended, deactivated or not yet verified. Only
    /// reachable with the correct password, so saying so reveals nothing to a stranger.
    /// </summary>
    NotAllowed = 3,

    /// <summary>
    /// A sign-in code could not be checked because the store that holds codes is unreachable. Only
    /// sign-in by code returns it; a password sign-in does not depend on that store.
    /// </summary>
    Unavailable = 4,
}

/// <summary>The outcome of a sign-in.</summary>
/// <param name="Failure"><see cref="SignInFailure.None"/> when signed in.</param>
/// <param name="Tokens">The issued pair, when signed in.</param>
/// <param name="Institutions">Every institution the user may act for.</param>
/// <param name="ActiveInstitution">
/// The one the token acts for: the only institution when there is exactly one, otherwise none until
/// the user chooses.
/// </param>
public sealed record SignInResult(
    SignInFailure Failure,
    IssuedTokens? Tokens,
    IReadOnlyList<InstitutionAccess> Institutions,
    InstitutionAccess? ActiveInstitution)
{
    /// <summary>True when tokens were issued.</summary>
    public bool Succeeded => Failure == SignInFailure.None;

    /// <summary>Signed in.</summary>
    public static SignInResult Success(
        IssuedTokens tokens,
        IReadOnlyList<InstitutionAccess> institutions,
        InstitutionAccess? activeInstitution) =>
        new(SignInFailure.None, tokens, institutions, activeInstitution);

    /// <summary>Refused, for <paramref name="failure"/>.</summary>
    public static SignInResult Failed(SignInFailure failure) => new(failure, null, [], null);
}

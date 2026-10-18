namespace Sethika.Application.Access;

/// <summary>
/// What a one-time code was issued for.
/// </summary>
/// <remarks>
/// The purpose is part of the Redis key, so a code issued for one flow cannot be presented to
/// another. Without it, a signup code - which an attacker can have sent to their own phone at
/// will - would also satisfy an MFA step-up challenge on an account they are attacking.
/// </remarks>
public enum OtpPurpose
{
    /// <summary>Proving control of a phone number while creating an account.</summary>
    Signup = 1,

    /// <summary>Signing in with a phone number instead of a password.</summary>
    Login = 2,

    /// <summary>Re-verifying an already-authenticated user before a sensitive action.</summary>
    StepUp = 3,

    /// <summary>Proving control of a phone number while resetting a forgotten password.</summary>
    Reset = 4,
}

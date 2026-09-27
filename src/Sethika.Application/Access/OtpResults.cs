namespace Sethika.Application.Access;

/// <summary>Why a request for a one-time code was refused, or that it was sent.</summary>
public enum OtpSendOutcome
{
    /// <summary>A code was generated and handed to the SMS sender.</summary>
    Sent = 0,

    /// <summary>A code was sent recently and the resend cooldown has not elapsed.</summary>
    CooldownActive = 1,

    /// <summary>This phone number has requested too many codes in the current window.</summary>
    RateLimited = 2,

    /// <summary>The store backing one-time codes is unreachable.</summary>
    Unavailable = 3,
}

/// <summary>The result of requesting a one-time code.</summary>
/// <param name="Outcome">Whether a code was sent, and if not, why not.</param>
/// <param name="RetryAfter">
/// How long the caller must wait before asking again. Present for
/// <see cref="OtpSendOutcome.CooldownActive"/> and <see cref="OtpSendOutcome.RateLimited"/>.
/// </param>
public readonly record struct OtpSendResult(OtpSendOutcome Outcome, TimeSpan? RetryAfter)
{
    /// <summary>True when a code was generated and handed to the sender.</summary>
    public bool WasSent => Outcome == OtpSendOutcome.Sent;

    /// <summary>A code was generated and handed to the sender.</summary>
    public static OtpSendResult Sent() => new(OtpSendOutcome.Sent, null);

    /// <summary>A code was sent too recently; the caller must wait.</summary>
    public static OtpSendResult Cooldown(TimeSpan retryAfter) =>
        new(OtpSendOutcome.CooldownActive, retryAfter);

    /// <summary>Too many codes requested for this number; the caller must wait.</summary>
    public static OtpSendResult RateLimited(TimeSpan retryAfter) =>
        new(OtpSendOutcome.RateLimited, retryAfter);

    /// <summary>The backing store is unreachable, so no code could be issued.</summary>
    public static OtpSendResult Unavailable() => new(OtpSendOutcome.Unavailable, null);
}

/// <summary>The outcome of presenting a one-time code.</summary>
public enum OtpVerifyOutcome
{
    /// <summary>The code matched and has been consumed. It cannot be presented again.</summary>
    Verified = 0,

    /// <summary>The code did not match. Further attempts may remain.</summary>
    Incorrect = 1,

    /// <summary>No code is outstanding for this number and purpose, or it has expired.</summary>
    NotFound = 2,

    /// <summary>
    /// Too many incorrect attempts. The code has been destroyed and a new one must be requested,
    /// so an attacker cannot keep guessing against a code that stays valid for its full lifetime.
    /// </summary>
    TooManyAttempts = 3,

    /// <summary>The store backing one-time codes is unreachable.</summary>
    Unavailable = 4,
}

/// <summary>The result of presenting a one-time code.</summary>
/// <param name="Outcome">Whether the code was accepted, and if not, why not.</param>
/// <param name="AttemptsRemaining">
/// Attempts left before the code is destroyed, when the outcome is
/// <see cref="OtpVerifyOutcome.Incorrect"/>.
/// </param>
public readonly record struct OtpVerifyResult(OtpVerifyOutcome Outcome, int AttemptsRemaining)
{
    /// <summary>True only when the code matched and was consumed.</summary>
    public bool IsVerified => Outcome == OtpVerifyOutcome.Verified;

    /// <summary>The code matched and has been consumed.</summary>
    public static OtpVerifyResult Verified() => new(OtpVerifyOutcome.Verified, 0);

    /// <summary>The code did not match; <paramref name="attemptsRemaining"/> attempts are left.</summary>
    public static OtpVerifyResult Incorrect(int attemptsRemaining) =>
        new(OtpVerifyOutcome.Incorrect, attemptsRemaining);

    /// <summary>No outstanding code for this number and purpose.</summary>
    public static OtpVerifyResult NotFound() => new(OtpVerifyOutcome.NotFound, 0);

    /// <summary>The attempt budget is exhausted and the code has been destroyed.</summary>
    public static OtpVerifyResult TooManyAttempts() => new(OtpVerifyOutcome.TooManyAttempts, 0);

    /// <summary>The backing store is unreachable, so the code could not be checked.</summary>
    public static OtpVerifyResult Unavailable() => new(OtpVerifyOutcome.Unavailable, 0);
}

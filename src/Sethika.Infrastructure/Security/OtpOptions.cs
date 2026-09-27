namespace Sethika.Infrastructure.Security;

/// <summary>
/// Tunables for one-time codes. Every default here is a trade-off between an attacker's guessing
/// budget, an SMS bill, and a user who did not receive the first message.
/// </summary>
public sealed class OtpOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Otp";

    /// <summary>Digits in a code. Six is the length users expect from a Sri Lankan bank SMS.</summary>
    /// <remarks>
    /// Six digits is one chance in a million per guess, which is only safe because
    /// <see cref="MaxVerifyAttempts"/> destroys the code long before that space can be searched.
    /// Length and the attempt cap are one control, not two - raising either in isolation is not
    /// what makes this safe.
    /// </remarks>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long a code stays valid.</summary>
    /// <remarks>
    /// Five minutes is long enough for a slow Sri Lankan SMS route and short enough that a code
    /// read off a lock screen an hour later is useless.
    /// </remarks>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wrong guesses allowed before the code is destroyed.</summary>
    /// <remarks>
    /// Five tolerates a mistyped digit; it does not tolerate a script. Once exhausted the code is
    /// deleted rather than left to expire, so an attacker cannot keep guessing for the remainder of
    /// the lifetime.
    /// </remarks>
    public int MaxVerifyAttempts { get; set; } = 5;

    /// <summary>Minimum wait before the same number may request another code.</summary>
    /// <remarks>
    /// Sixty seconds is roughly when a user decides the message is not coming and presses resend.
    /// Shorter and the resend button becomes a way to bill us; longer and a genuinely lost message
    /// leaves them stuck.
    /// </remarks>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a verification ticket may wait to be redeemed.</summary>
    /// <remarks>
    /// Fifteen minutes is the time to fill in a signup form after the code arrives - name, email,
    /// password, reading the terms - without asking for a second SMS. The ticket is single-use, so
    /// its lifetime bounds only how long a stolen one is worth anything.
    /// </remarks>
    public TimeSpan TicketLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Codes one number may request per <see cref="SendWindow"/>.</summary>
    public int MaxSendsPerWindow { get; set; } = 3;

    /// <summary>Window over which <see cref="MaxSendsPerWindow"/> is counted.</summary>
    /// <remarks>
    /// This is the control that bounds the bill. Each SMS costs money and nothing upstream of it
    /// requires authentication, so without a per-number cap a loop against the signup form is a
    /// direct charge to the project.
    /// </remarks>
    public TimeSpan SendWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Writes the generated code to the log at information level. Development only.
    /// </summary>
    /// <remarks>
    /// This is how the console sender flow is followed without an SMS gateway. It puts a live
    /// credential in the log, so the host refuses to start with it enabled outside Development.
    /// </remarks>
    public bool LogCodesForDevelopment { get; set; }
}

using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>
/// The limits on the anonymous auth endpoints (plan 5.4), on step-up (5.3), which shares the two
/// OTP client budgets, and on password change. One place, so the numbers can be read and argued
/// with together.
/// </summary>
/// <remarks>
/// Two kinds of subject, doing different jobs:
///
/// Per identity - a login name, a phone number. These are the real controls: they bound guessing
/// against one account or one code however many machines the guesses come from, and they apply to
/// logins with no account exactly as to logins with one, so the limit itself says nothing about
/// which accounts exist.
///
/// Per client address. These only blunt a single machine spraying many identities, and they are
/// deliberately loose: Sri Lankan mobile networks put many subscribers behind one address (carrier
/// NAT), and a tight per-address limit would lock out a whole temple's donors at once. The address is
/// only as good as the forwarded-headers configuration - see <c>ForwardedHeaders:KnownProxies</c>.
///
/// The per-number OTP send cap and the account lockout sit behind these and do not depend on them.
/// </remarks>
internal static class AuthRateLimits
{
    /// <summary>SMS spraying from one address. Each number is also capped by OtpService.</summary>
    public static readonly RateLimitRule OtpRequestPerClient = new("otp-request-client", 20, TimeSpan.FromHours(1));

    /// <summary>
    /// Sign-in codes requested from one address. The signup budget's twin under its own key, so
    /// neither flow can spend the other's.
    /// </summary>
    public static readonly RateLimitRule OtpLoginRequestPerClient = new("otp-login-request-client", 20, TimeSpan.FromHours(1));

    /// <summary>
    /// Sign-in guesses against one number's codes, across resends - the twin of
    /// <see cref="OtpVerifyPerPhone"/>. It counts numbers with no account exactly like the rest.
    /// </summary>
    public static readonly RateLimitRule OtpSignInPerPhone = new("otp-signin-phone", 10, TimeSpan.FromMinutes(15));

    /// <summary>Sign-in guesses against many numbers from one address - the twin of <see cref="OtpVerifyPerClient"/>.</summary>
    public static readonly RateLimitRule OtpSignInPerClient = new("otp-signin-client", 60, TimeSpan.FromMinutes(15));

    /// <summary>Guesses against codes for one number, across resends.</summary>
    public static readonly RateLimitRule OtpVerifyPerPhone = new("otp-verify-phone", 10, TimeSpan.FromMinutes(15));

    /// <summary>Guessing codes for many numbers from one address.</summary>
    public static readonly RateLimitRule OtpVerifyPerClient = new("otp-verify-client", 60, TimeSpan.FromMinutes(15));

    /// <summary>
    /// The three OTP budgets again, for password-reset codes. Their own counters, so a burst of
    /// signups from one address cannot use up the budget its users need to recover an account, nor
    /// the reverse.
    /// </summary>
    public static readonly RateLimitRule OtpResetRequestPerClient = new("otp-reset-request-client", 20, TimeSpan.FromHours(1));

    /// <inheritdoc cref="OtpResetRequestPerClient"/>
    public static readonly RateLimitRule OtpResetVerifyPerPhone = new("otp-reset-verify-phone", 10, TimeSpan.FromMinutes(15));

    /// <inheritdoc cref="OtpResetRequestPerClient"/>
    public static readonly RateLimitRule OtpResetVerifyPerClient = new("otp-reset-verify-client", 60, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Setting a password from one address. Not a guessing control - the ticket is 256 random bits -
    /// but every attempt that passes the ticket check costs a 210 000-iteration hash.
    /// </summary>
    public static readonly RateLimitRule PasswordResetPerClient = new("password-reset-client", 20, TimeSpan.FromHours(1));

    /// <summary>Account creation from one address.</summary>
    public static readonly RateLimitRule SignUpPerClient = new("signup-client", 20, TimeSpan.FromHours(1));

    /// <summary>
    /// Password guesses against one login, whether or not it has an account. Above the lockout's
    /// five, so a real account locks first and this catches the logins lockout cannot see.
    /// </summary>
    public static readonly RateLimitRule LoginPerName = new("login-name", 10, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Refreshes, institution switches and logouts from one address. Not a guessing control - a refresh token
    /// is 256 random bits - but a bound on load: every open tab refreshes every fifteen minutes and
    /// once per page load, and many users share an address.
    /// </summary>
    public static readonly RateLimitRule RefreshPerClient = new("refresh-client", 300, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Password changes by one signed-in user. Above the lockout's five, like the per-login limit, so
    /// wrong current passwords lock the account first; this bounds the PBKDF2 work one access token can
    /// ask for, since a correct password costs two hashes and the lockout never sees it.
    /// </summary>
    public static readonly RateLimitRule PasswordChangePerUser = new("password-change-user", 10, TimeSpan.FromMinutes(15));

    /// <summary>Password spraying across many logins from one address.</summary>
    public static readonly RateLimitRule LoginPerClient = new("login-client", 100, TimeSpan.FromMinutes(15));
}

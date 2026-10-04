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

    /// <summary>Guesses against codes for one number, across resends.</summary>
    public static readonly RateLimitRule OtpVerifyPerPhone = new("otp-verify-phone", 10, TimeSpan.FromMinutes(15));

    /// <summary>Guessing codes for many numbers from one address.</summary>
    public static readonly RateLimitRule OtpVerifyPerClient = new("otp-verify-client", 60, TimeSpan.FromMinutes(15));

    /// <summary>Account creation from one address.</summary>
    public static readonly RateLimitRule SignUpPerClient = new("signup-client", 20, TimeSpan.FromHours(1));

    /// <summary>
    /// Password guesses against one login, whether or not it has an account. Above the lockout's
    /// five, so a real account locks first and this catches the logins lockout cannot see.
    /// </summary>
    public static readonly RateLimitRule LoginPerName = new("login-name", 10, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Refreshes and institution switches from one address. Not a guessing control - a refresh token
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

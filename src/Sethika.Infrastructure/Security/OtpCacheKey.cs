using Sethika.Application.Access;

namespace Sethika.Infrastructure.Security;

/// <summary>
/// Builds the Redis keys one-time codes live under.
/// </summary>
/// <remarks>
/// The purpose is part of every key. Without it there is one code per phone number, and a code an
/// attacker had sent to their own phone for signup would also satisfy an MFA step-up challenge on
/// an account they are attacking - the step-up would verify that someone holds a code, not that the
/// account holder does.
///
/// Keys are prefixed <c>otp:</c> so they are distinguishable from <c>perm:</c> entries in a shared
/// Redis database, and the send counter is kept separately from the code because they expire on
/// different clocks: the code after its short lifetime, the counter at the end of the rate window.
/// </remarks>
public static class OtpCacheKey
{
    /// <summary>The hash holding the code itself, its attempt count, and nothing else.</summary>
    public static string ForCode(string phoneNumber, OtpPurpose purpose) =>
        $"otp:{Slug(purpose)}:{phoneNumber}";

    /// <summary>The counter bounding how many codes a number may request per window.</summary>
    public static string ForSendCount(string phoneNumber, OtpPurpose purpose) =>
        $"otp:sends:{Slug(purpose)}:{phoneNumber}";

    /// <summary>
    /// The lowercase name used in keys.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from <c>ToString</c> so that renaming an enum member - a
    /// refactor no reviewer would look twice at - cannot silently change a key and invalidate every
    /// outstanding code, or worse, collide with another purpose's keyspace.
    /// </remarks>
    private static string Slug(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.Signup => "signup",
        OtpPurpose.Login => "login",
        OtpPurpose.StepUp => "stepup",
        OtpPurpose.Reset => "reset",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown OTP purpose."),
    };
}

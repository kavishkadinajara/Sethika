using System.Text;

namespace Sethika.Application.Access.SignIn;

/// <summary>A password sign-in attempt.</summary>
/// <param name="Login">A phone number in any common spelling, or an email address.</param>
/// <param name="Password">The password as typed.</param>
/// <param name="IpAddress">The caller's address, for <c>auth.LoginHistory</c>.</param>
/// <param name="UserAgent">The caller's user agent, for <c>auth.LoginHistory</c> and the refresh token row.</param>
public sealed record SignInCommand(string? Login, string? Password, string? IpAddress, string? UserAgent)
{
    /// <summary>
    /// Leaves the password out of <see cref="object.ToString"/>.
    /// </summary>
    /// <remarks>
    /// A record prints every property by default, so logging a command - or an exception message
    /// that includes one - would write the plaintext password to the log.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Login = ").Append(Login)
               .Append(", IpAddress = ").Append(IpAddress)
               .Append(", UserAgent = ").Append(UserAgent);

        return true;
    }
}

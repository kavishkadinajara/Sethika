using System.Text;

namespace Sethika.Application.Access.SignIn;

/// <summary>A sign-in with a code sent by SMS instead of a password.</summary>
/// <param name="PhoneNumber">The number the code was sent to, in E.164 form.</param>
/// <param name="Code">The code as typed.</param>
/// <param name="IpAddress">The caller's address, for <c>auth.LoginHistory</c>.</param>
/// <param name="UserAgent">The caller's user agent, for <c>auth.LoginHistory</c> and the refresh token row.</param>
public sealed record OtpSignInCommand(string PhoneNumber, string? Code, string? IpAddress, string? UserAgent)
{
    /// <summary>Leaves the code out of <see cref="object.ToString"/>: until it is spent it is a credential.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber)
               .Append(", IpAddress = ").Append(IpAddress)
               .Append(", UserAgent = ").Append(UserAgent);

        return true;
    }
}

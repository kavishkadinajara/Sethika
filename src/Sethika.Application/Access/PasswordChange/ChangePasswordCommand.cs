using System.Text;

namespace Sethika.Application.Access.PasswordChange;

/// <summary>A signed-in user's request to replace their own password.</summary>
/// <param name="UserId">The user, from the validated access token - never from the request body.</param>
/// <param name="CurrentPassword">The password as the user typed it, proving they know it.</param>
/// <param name="NewPassword">The replacement, as typed.</param>
/// <param name="ActiveInstitutionId">
/// The institution the calling device's token acts for, so its new session keeps acting for it.
/// </param>
/// <param name="IpAddress">The caller's address, for <c>auth.LoginHistory</c> and the refresh token row.</param>
/// <param name="UserAgent">The caller's user agent, for the same rows.</param>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string? CurrentPassword,
    string? NewPassword,
    int? ActiveInstitutionId,
    string? IpAddress,
    string? UserAgent)
{
    /// <summary>Leaves both passwords out of <see cref="object.ToString"/>.</summary>
    /// <remarks>
    /// A record prints every property by default, so logging a command - or an exception message
    /// that includes one - would write two plaintext passwords to the log.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserId = ").Append(UserId)
               .Append(", ActiveInstitutionId = ").Append(ActiveInstitutionId)
               .Append(", IpAddress = ").Append(IpAddress)
               .Append(", UserAgent = ").Append(UserAgent);

        return true;
    }
}

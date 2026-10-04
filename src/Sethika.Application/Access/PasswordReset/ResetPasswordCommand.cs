using System.Text;

namespace Sethika.Application.Access.PasswordReset;

/// <summary>Sets a new password with the ticket a verified reset code was exchanged for.</summary>
/// <param name="Ticket">The reset ticket, as the verification returned it.</param>
/// <param name="NewPassword">Checked against the same policy signup uses.</param>
public sealed record ResetPasswordCommand(string? Ticket, string? NewPassword)
{
    /// <summary>Leaves the ticket and the password - both secrets - out of <see cref="object.ToString"/>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Ticket = ").Append(Ticket is null ? "null" : "(redacted)")
               .Append(", NewPassword = ").Append(NewPassword is null ? "null" : "(redacted)");
        return true;
    }
}

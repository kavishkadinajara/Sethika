using System.Text;

namespace Sethika.Application.Access.Bootstrap;

/// <summary>The first platform administrator, as configured in User Secrets (plan Q5).</summary>
/// <param name="PhoneNumber">Any common spelling; normalised to E.164.</param>
/// <param name="Password">Checked against the password policy.</param>
/// <param name="FullName">The administrator's name.</param>
public sealed record BootstrapPlatformAdminCommand(string? PhoneNumber, string? Password, string? FullName)
{
    /// <summary>Leaves the password out of <see cref="object.ToString"/>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PhoneNumber = ").Append(PhoneNumber).Append(", FullName = ").Append(FullName);
        return true;
    }
}

/// <summary>What the bootstrap did.</summary>
public enum BootstrapOutcome
{
    /// <summary>A new platform account was created and made administrator.</summary>
    Created = 0,

    /// <summary>An existing platform account without the role was given it.</summary>
    RoleAssigned = 1,

    /// <summary>This account is already the administrator; nothing was changed, not even the password.</summary>
    AlreadyAdministrator = 2,

    /// <summary>Another active account is administrator. Later administrators are made in the app.</summary>
    AnotherAdministratorExists = 3,

    /// <summary>The number belongs to a donor or institution account, which is not converted.</summary>
    NotAPlatformAccount = 4,

    /// <summary>The number belongs to a suspended, deactivated or deleted account.</summary>
    AccountNotActive = 5,

    InvalidPhoneNumber = 6,

    InvalidFullName = 7,

    /// <summary>See <see cref="BootstrapPlatformAdminResult.PasswordFailures"/>.</summary>
    PasswordRejected = 8,
}

/// <summary>The outcome, and for a rejected password, why.</summary>
public sealed record BootstrapPlatformAdminResult(BootstrapOutcome Outcome, PasswordFailure PasswordFailures = PasswordFailure.None)
{
    /// <summary>True when an administrator exists for this number afterwards.</summary>
    public bool Succeeded => Outcome is BootstrapOutcome.Created or BootstrapOutcome.RoleAssigned or BootstrapOutcome.AlreadyAdministrator;
}

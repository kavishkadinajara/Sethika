using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.Bootstrap;

/// <summary>
/// Creates the first platform administrator - plan Q5. Run once per environment by
/// <c>dotnet run --project src/Sethika.Api -- bootstrap</c>, with the details in User Secrets, so the
/// credential is never in source control or in the dacpac, where CLAUDE.md forbids business data.
/// </summary>
/// <remarks>
/// Idempotent: run again with the same number it changes nothing, not even the password, which
/// the administrator may since have changed. It makes only the first administrator - once an active
/// one exists, the next is made in the app by someone accountable - and it never converts a donor
/// or institution account, whose owner did not ask to become one.
///
/// If every administrator is suspended or deleted, there is no active one, so the command works
/// again: that is the recovery path, open to whoever holds the production secrets.
/// </remarks>
public sealed class BootstrapPlatformAdminHandler(
    ISethikaDbContext db,
    IPhoneNumberNormalizer phoneNumbers,
    IPasswordPolicy passwordPolicy,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
{
    private const int FullNameMaxLength = 150;
    private const string AdministratorRoleCode = "PLATFORM_ADMIN";
    private const string PlatformStaffUserType = "PlatformStaff";
    private const string ActiveStatus = "Active";

    public async Task<BootstrapPlatformAdminResult> HandleAsync(
        BootstrapPlatformAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!phoneNumbers.TryNormalize(command.PhoneNumber, out var phoneNumber))
        {
            return new(BootstrapOutcome.InvalidPhoneNumber);
        }

        var fullName = command.FullName?.Trim();
        if (string.IsNullOrEmpty(fullName) || fullName.Length > FullNameMaxLength)
        {
            return new(BootstrapOutcome.InvalidFullName);
        }

        var roleId = await db.Roles
            .AsNoTracking()
            .Where(r => r.InstitutionId == null && r.Code == AdministratorRoleCode && r.IsActive)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The PLATFORM_ADMIN role is missing: the database was published without Scripts/Seed/08_SystemRoles.sql.");

        var now = timeProvider.GetUtcNow();
        var at = now.UtcDateTime;

        var administrators = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == roleId
                         && ur.IsActive
                         && (ur.ValidFrom == null || ur.ValidFrom <= at)
                         && (ur.ValidTo == null || ur.ValidTo > at))
            .Join(db.Users.Where(u => !u.IsDeleted && u.Status == ActiveStatus), ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await db.Users
            .Where(u => u.PhoneNumber == phoneNumber && !u.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null && administrators.Contains(existing.Id))
        {
            return new(BootstrapOutcome.AlreadyAdministrator);
        }

        if (administrators.Count > 0)
        {
            return new(BootstrapOutcome.AnotherAdministratorExists);
        }

        if (existing is not null)
        {
            return await AssignToExistingAsync(existing, roleId, at, cancellationToken).ConfigureAwait(false);
        }

        var policy = passwordPolicy.Validate(command.Password, [phoneNumber, fullName]);
        if (!policy.IsValid)
        {
            return new(BootstrapOutcome.PasswordRejected, policy.Failures);
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(now),
            UserType = PlatformStaffUserType,
            FullName = fullName,
            PhoneNumber = phoneNumber,

            // Vouched for by whoever holds the environment's secrets. It has to be confirmed: it is
            // where step-up codes go, and an administrator who cannot step up cannot administer.
            PhoneConfirmed = true,
            MfaRequired = true,
            PasswordChangedAt = at,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PermissionsVersion = 1,
            PreferredLanguage = "en",
            Status = ActiveStatus,
            CreatedAt = at,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password!);

        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId, IsActive = true });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new(BootstrapOutcome.Created);
    }

    private async Task<BootstrapPlatformAdminResult> AssignToExistingAsync(
        User user,
        int roleId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(user.UserType, PlatformStaffUserType, StringComparison.Ordinal))
        {
            return new(BootstrapOutcome.NotAPlatformAccount);
        }

        if (!string.Equals(user.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return new(BootstrapOutcome.AccountNotActive);
        }

        // An active row can only be here if its validity window has lapsed, and UX_UserRoles allows
        // one active row per user and role - so that row is reopened rather than a second added.
        var lapsed = await db.UserRoles
            .SingleOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == roleId && ur.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (lapsed is null)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId, IsActive = true });
        }
        else
        {
            lapsed.ValidFrom = null;
            lapsed.ValidTo = null;
        }

        // A new role is a permissions change, and the version is in the cache key (CLAUDE.md).
        user.PermissionsVersion++;
        user.UpdatedAt = at;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(BootstrapOutcome.RoleAssigned);
    }
}

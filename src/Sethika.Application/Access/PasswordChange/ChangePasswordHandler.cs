using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Session;
using Sethika.Application.Access.SignIn;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.PasswordChange;

/// <summary>
/// Replaces a signed-in user's password, ends every other session they have, and keeps the calling
/// device signed in.
/// </summary>
/// <remarks>
/// The current password is checked by <see cref="PasswordVerifier"/>, the code sign-in uses, so a
/// wrong one counts toward the same lockout. Without that, a stolen access token would be fifteen
/// minutes of unlimited password guessing through this endpoint, beside a sign-in that allows five.
///
/// The current password is verified before anything is said about the new one. The policy compares
/// the new password against the user's own phone, email and name, so answering it first would let a
/// token holder who does not know the password probe those values through the policy's error.
///
/// One transaction writes the new hash, rotates the security stamp - which revokes every refresh
/// token issued before it, the first of the duties plan A1 took over from <c>UserManager</c> - resets
/// the failure count, adds the outbox event, and starts the calling device's new session through
/// <see cref="SessionStarter"/>. The session is added after the rotation, so it carries the new
/// stamp and is not among the tokens the rotation revokes. Every other device keeps its access token
/// until it expires (plan A2) and loses its session at its next refresh.
/// </remarks>
public sealed class ChangePasswordHandler(
    ISethikaDbContext db,
    PasswordVerifier passwords,
    SessionStarter sessions,
    IPasswordPolicy passwordPolicy,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
{
    /// <summary>The outbox event a password change writes.</summary>
    public const string ChangedEvent = "PasswordChanged";

    private sealed record Holder(
        PasswordAccount Account,
        string? PhoneNumber,
        string? Email,
        string? NormalizedEmail,
        string FullName);

    /// <summary>Changes the password, or says why not.</summary>
    public async Task<ChangePasswordResult> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var current = command.CurrentPassword ?? string.Empty;
        var replacement = command.NewPassword ?? string.Empty;

        var holder = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == command.UserId && !u.IsDeleted)
            .Select(u => new Holder(
                new PasswordAccount(u.Id, u.PasswordHash, u.LockoutEnd, u.Status, u.UserType, u.SecurityStamp),
                u.PhoneNumber,
                u.Email,
                u.NormalizedEmail,
                u.FullName))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (holder is null)
        {
            // A deleted account whose access token has not yet expired (plan A2).
            return ChangePasswordResult.Failed(ChangePasswordFailure.AccountNotActive);
        }

        // Recorded against the account's own login, as step-up records its rows: the request names none.
        var loginName = holder.PhoneNumber ?? holder.NormalizedEmail ?? holder.Account.Id.ToString();

        var verification = await passwords
            .VerifyAsync(holder.Account, loginName, current, command.IpAddress, command.UserAgent, now, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.Succeeded)
        {
            return ChangePasswordResult.Failed(verification.Failure switch
            {
                SignInFailure.LockedOut => ChangePasswordFailure.LockedOut,
                SignInFailure.NotAllowed => ChangePasswordFailure.AccountNotActive,
                _ => ChangePasswordFailure.PasswordIncorrect,
            });
        }

        // The current password has just verified, so plaintext equality is the same question as
        // "does the new one verify against the stored hash?" without a second PBKDF2 run.
        if (string.Equals(current, replacement, StringComparison.Ordinal))
        {
            return ChangePasswordResult.Failed(ChangePasswordFailure.PasswordUnchanged);
        }

        var policy = passwordPolicy.Validate(replacement, [holder.PhoneNumber, holder.Email, holder.FullName]);
        if (!policy.IsValid)
        {
            return ChangePasswordResult.Failed(ChangePasswordFailure.PasswordRejected, policy.Failures);
        }

        var hash = passwordHasher.HashPassword(new User { Id = holder.Account.Id }, replacement);
        var at = now.UtcDateTime;

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The reset is the same duty sign-in carries (plan A1): failures typed before the right
        // password must not survive into the new one's life and lock the user out later.
        await db.Users
            .Where(u => u.Id == holder.Account.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordChangedAt, at)
                    .SetProperty(u => u.AccessFailedCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);

        var stamp = await SecurityStamps
            .RotateAsync(db, holder.Account.Id, SessionHandler.RevokedReasons.PasswordChanged, at, cancellationToken)
            .ConfigureAwait(false);

        // The id only, as every outbox row so far: a consumer that tells the user their password
        // changed looks up where to send it, so the outbox holds no PII.
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = ChangedEvent,
            AggregateType = "User",
            AggregateId = holder.Account.Id.ToString(),
            Payload = JsonSerializer.Serialize(new { userId = holder.Account.Id }),
            OccurredAt = at,
        });

        var session = await sessions
            .StartAsync(
                holder.Account with { SecurityStamp = stamp },
                loginName,
                command.ActiveInstitutionId,
                command.IpAddress,
                command.UserAgent,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ChangePasswordResult.Success(session);
    }
}

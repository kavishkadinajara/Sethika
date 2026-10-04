using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// Checks a password against an account, applying and counting toward the lockout, and records every
/// refusal in <c>auth.LoginHistory</c>.
/// </summary>
/// <remarks>
/// Sign-in and password change both ask "is this the account's password?", and both must count a
/// wrong answer toward the same lockout. One class, so there is one lockout: a second copy for
/// password change would be a second guessing budget against the same account the first time either
/// copy drifted.
///
/// No account enumeration. An unknown login is verified against <see cref="DummyPasswordHash"/>, and
/// a locked account still verifies the password before refusing, so neither answer arrives sooner
/// than a wrong password does.
///
/// A lockout that concurrency cannot defeat. Failures are counted by one UPDATE that increments in
/// the database. Reading the count and writing count + 1 would let fifty parallel wrong guesses all
/// read 0 and all write 1, so an attacker could guess indefinitely in bursts.
///
/// A success writes nothing: what a success changes - the reset, a session, a new hash - belongs in
/// the caller's transaction.
/// </remarks>
public sealed class PasswordVerifier(
    ISethikaDbContext db,
    IPasswordHasher<User> passwordHasher,
    DummyPasswordHash dummyPasswordHash,
    IOptions<IdentityOptions> identityOptions)
{
    private const string ActiveStatus = "Active";

    private static class FailureReasons
    {
        public const string UnknownLogin = "UnknownLogin";
        public const string NoPassword = "NoPassword";
        public const string LockedOut = "LockedOut";
        public const string InvalidPassword = "InvalidPassword";
        public const string AccountNotActive = "AccountNotActive";
    }

    /// <summary>Verifies <paramref name="password"/> and records a refusal, whatever its reason.</summary>
    /// <param name="account">The account the login matched, or null when it matched none.</param>
    /// <param name="loginName">What the history row records the attempt against.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="ipAddress">The caller's address, for the history row.</param>
    /// <param name="userAgent">The caller's user agent, for the history row.</param>
    /// <param name="now">The time of the attempt.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    public async Task<PasswordVerification> VerifyAsync(
        PasswordAccount? account,
        string loginName,
        string? password,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        password ??= string.Empty;

        if (account?.PasswordHash is not { } storedHash)
        {
            // Same work as a real verification, and the same answer as a wrong password.
            passwordHasher.VerifyHashedPassword(new User(), dummyPasswordHash.Value, password);

            await RecordAsync(
                LoginAttempts.Failed(account?.Id, loginName, account is null ? FailureReasons.UnknownLogin : FailureReasons.NoPassword, ipAddress, userAgent, now),
                cancellationToken).ConfigureAwait(false);

            return PasswordVerification.Refused(SignInFailure.InvalidCredentials);
        }

        // Verified before the lockout check on purpose, so a locked account costs the same time to
        // refuse as any other failure. The result is not used when the account is locked.
        var verification = passwordHasher.VerifyHashedPassword(new User { Id = account.Id }, storedHash, password);

        if (account.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            await RecordAsync(LoginAttempts.Failed(account.Id, loginName, FailureReasons.LockedOut, ipAddress, userAgent, now), cancellationToken)
                .ConfigureAwait(false);

            return PasswordVerification.Refused(SignInFailure.LockedOut);
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(
                account.Id,
                LoginAttempts.Failed(account.Id, loginName, FailureReasons.InvalidPassword, ipAddress, userAgent, now),
                now,
                cancellationToken).ConfigureAwait(false);

            return PasswordVerification.Refused(SignInFailure.InvalidCredentials);
        }

        if (!string.Equals(account.Status, ActiveStatus, StringComparison.Ordinal))
        {
            await RecordAsync(LoginAttempts.Failed(account.Id, loginName, FailureReasons.AccountNotActive, ipAddress, userAgent, now), cancellationToken)
                .ConfigureAwait(false);

            return PasswordVerification.Refused(SignInFailure.NotAllowed);
        }

        return PasswordVerification.Verified(verification == PasswordVerificationResult.SuccessRehashNeeded);
    }

    /// <summary>Counts one failure, locking the account when it reaches the limit.</summary>
    private async Task RegisterFailureAsync(
        Guid userId,
        LoginAttempt attempt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lockout = identityOptions.Value.Lockout;
        var maxFailures = lockout.MaxFailedAccessAttempts;
        DateTimeOffset? lockedUntil = now + lockout.DefaultLockoutTimeSpan;

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Both SET expressions read the row as it was before this UPDATE, and SQL Server takes the
        // row lock for the statement, so concurrent failures are serialised and each one counts.
        // Reaching the limit locks the account and starts the count again, as Identity does.
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1 >= maxFailures ? 0 : u.AccessFailedCount + 1)
                    .SetProperty(u => u.LockoutEnd, u => u.AccessFailedCount + 1 >= maxFailures ? lockedUntil : u.LockoutEnd),
                cancellationToken)
            .ConfigureAwait(false);

        db.LoginAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(LoginAttempt attempt, CancellationToken cancellationToken)
    {
        db.LoginAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

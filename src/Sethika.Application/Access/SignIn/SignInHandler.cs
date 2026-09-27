using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Session;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// Signs a user in with a phone number or email and a password.
/// </summary>
/// <remarks>
/// Three properties this handler exists to keep, each with its own mechanism:
///
/// No account enumeration. An unknown login and a wrong password return the same failure and cost
/// the same PBKDF2 work - an unknown login is verified against <see cref="DummyPasswordHash"/> - and
/// a locked account still verifies the password before refusing, so its answer is not faster either.
///
/// A lockout that concurrency cannot defeat. Failures are counted by one UPDATE that increments in
/// the database. Reading the count and writing count + 1 would let fifty parallel wrong guesses all
/// read 0 and all write 1, so an attacker could guess indefinitely in bursts.
///
/// One transaction per outcome. The failure count and its history row, or the reset, the refresh
/// token and the success row, commit together or not at all - so the audit trail never records a
/// sign-in whose token was never stored, and a failure is never counted without a record of it.
/// </remarks>
public sealed class SignInHandler(
    ISethikaDbContext db,
    IPasswordHasher<User> passwordHasher,
    DummyPasswordHash dummyPasswordHash,
    IPhoneNumberNormalizer phoneNumbers,
    ITokenService tokens,
    IOptions<IdentityOptions> identityOptions,
    TimeProvider timeProvider)
{
    private const string ActiveStatus = "Active";
    private const string PlatformStaffUserType = "PlatformStaff";

    private static class FailureReasons
    {
        public const string UnknownLogin = "UnknownLogin";
        public const string NoPassword = "NoPassword";
        public const string LockedOut = "LockedOut";
        public const string InvalidPassword = "InvalidPassword";
        public const string AccountNotActive = "AccountNotActive";
    }

    private sealed record Account(
        Guid Id,
        string? PasswordHash,
        DateTimeOffset? LockoutEnd,
        string Status,
        string UserType,
        string SecurityStamp);

    /// <summary>Attempts the sign-in and records the attempt, whatever the outcome.</summary>
    public async Task<SignInResult> HandleAsync(
        SignInCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var password = command.Password ?? string.Empty;

        var (loginName, account) = await FindAccountAsync(command.Login, cancellationToken)
            .ConfigureAwait(false);

        if (account?.PasswordHash is not { } storedHash)
        {
            // Same work as a real verification, and the same answer as a wrong password.
            passwordHasher.VerifyHashedPassword(new User(), dummyPasswordHash.Value, password);

            await RecordAsync(
                Attempt(account?.Id, loginName, account is null ? FailureReasons.UnknownLogin : FailureReasons.NoPassword, command, now),
                cancellationToken).ConfigureAwait(false);

            return SignInResult.Failed(SignInFailure.InvalidCredentials);
        }

        // Verified before the lockout check on purpose, so a locked account costs the same time to
        // refuse as any other failure. The result is not used when the account is locked.
        var verification = passwordHasher.VerifyHashedPassword(new User { Id = account.Id }, storedHash, password);

        if (account.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            await RecordAsync(Attempt(account.Id, loginName, FailureReasons.LockedOut, command, now), cancellationToken)
                .ConfigureAwait(false);

            return SignInResult.Failed(SignInFailure.LockedOut);
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(account.Id, Attempt(account.Id, loginName, FailureReasons.InvalidPassword, command, now), now, cancellationToken)
                .ConfigureAwait(false);

            return SignInResult.Failed(SignInFailure.InvalidCredentials);
        }

        if (!string.Equals(account.Status, ActiveStatus, StringComparison.Ordinal))
        {
            await RecordAsync(Attempt(account.Id, loginName, FailureReasons.AccountNotActive, command, now), cancellationToken)
                .ConfigureAwait(false);

            return SignInResult.Failed(SignInFailure.NotAllowed);
        }

        // A hash made under an older, cheaper configuration is replaced while the plaintext is in
        // hand - the only moment it can be. This is how existing hashes move to the current
        // iteration count without a reset.
        var rehashed = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(new User { Id = account.Id }, password)
            : null;

        return await CompleteAsync(account, loginName, rehashed, command, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(string LoginName, Account? Account)> FindAccountAsync(
        string? login,
        CancellationToken cancellationToken)
    {
        // The same normalisation the sign-in rate limit keys by, so the two cannot disagree.
        var normalized = LoginName.Normalize(login, phoneNumbers);
        var value = normalized.Value;

        var account = normalized.Kind switch
        {
            LoginKind.Email => await FindAsync(u => u.NormalizedEmail == value, cancellationToken).ConfigureAwait(false),
            LoginKind.Phone => await FindAsync(u => u.PhoneNumber == value, cancellationToken).ConfigureAwait(false),
            _ => null,
        };

        return (value, account);
    }

    private Task<Account?> FindAsync(Expression<Func<User, bool>> match, CancellationToken cancellationToken) =>
        db.Users
          .AsNoTracking()
          .Where(match)
          .Where(u => !u.IsDeleted)
          .Select(u => new Account(u.Id, u.PasswordHash, u.LockoutEnd, u.Status, u.UserType, u.SecurityStamp))
          .SingleOrDefaultAsync(cancellationToken);

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

    private async Task<SignInResult> CompleteAsync(
        Account account,
        string loginName,
        string? rehashed,
        SignInCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var institutions = await UserInstitutions.LoadAsync(db, account.Id, now, cancellationToken).ConfigureAwait(false);

        // One institution is the common case - an owner with one temple - and a chooser screen with
        // one option is a pointless click. With several, the token acts for none until the user
        // picks one through switch-institution, which re-checks it.
        var activeInstitution = institutions.Count == 1 ? institutions[0] : null;
        var activeInstitutionId = activeInstitution?.Id;

        var issued = tokens.Issue(new TokenSubject(
            account.Id,
            activeInstitutionId,
            IsPlatform: string.Equals(account.UserType, PlatformStaffUserType, StringComparison.Ordinal)));

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Resetting the failure count here is one of the three duties plan A1 took over from
        // UserManager. Without it, failures from different days accumulate until a user who types
        // the right password is locked out anyway.
        await db.Users
            .Where(u => u.Id == account.Id)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters.SetProperty(u => u.AccessFailedCount, 0);
                    setters.SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null);
                    setters.SetProperty(u => u.LastLoginAt, now.UtcDateTime);

                    if (rehashed is not null)
                    {
                        setters.SetProperty(u => u.PasswordHash, rehashed);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        // A new family: every refresh from here on rotates within it, and a replayed member ends it.
        db.RefreshTokens.Add(RefreshTokenRows.New(
            issued,
            account.Id,
            familyId: Guid.NewGuid(),
            activeInstitutionId,
            account.SecurityStamp,
            command.IpAddress,
            command.UserAgent,
            now));

        db.LoginAttempts.Add(Attempt(account.Id, loginName, failureReason: null, command, now));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return SignInResult.Success(issued, institutions, activeInstitution);
    }

    private async Task RecordAsync(LoginAttempt attempt, CancellationToken cancellationToken)
    {
        db.LoginAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LoginAttempt Attempt(
        Guid? userId,
        string loginName,
        string? failureReason,
        SignInCommand command,
        DateTimeOffset now) => new()
        {
            UserId = userId,
            LoginName = Truncate(loginName, 256)!,
            Succeeded = failureReason is null,
            FailureReason = failureReason,
            IpAddress = Truncate(command.IpAddress, 45),
            UserAgent = Truncate(command.UserAgent, 400),
            AttemptedAt = now.UtcDateTime,
        };

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

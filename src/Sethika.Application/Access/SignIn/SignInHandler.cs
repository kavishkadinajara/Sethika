using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// Signs a user in with a phone number or email and a password.
/// </summary>
/// <remarks>
/// Three properties this handler exists to keep. The first two belong to <see cref="PasswordVerifier"/>,
/// which password change shares, so both count toward one lockout:
///
/// No account enumeration. An unknown login and a wrong password return the same failure and cost
/// the same PBKDF2 work, and a locked account still verifies the password before refusing.
///
/// A lockout that concurrency cannot defeat - failures are counted by one atomic UPDATE.
///
/// One transaction per outcome. The failure count and its history row, or the reset, the refresh
/// token and the success row, commit together or not at all - so the audit trail never records a
/// sign-in whose token was never stored, and a failure is never counted without a record of it.
/// </remarks>
public sealed class SignInHandler(
    ISethikaDbContext db,
    IPasswordHasher<User> passwordHasher,
    IPhoneNumberNormalizer phoneNumbers,
    PasswordVerifier passwords,
    SessionStarter sessions,
    TimeProvider timeProvider)
{
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

        var verification = await passwords
            .VerifyAsync(account, loginName, password, command.IpAddress, command.UserAgent, now, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.Succeeded)
        {
            return SignInResult.Failed(verification.Failure);
        }

        // A hash made under an older, cheaper configuration is replaced while the plaintext is in
        // hand - the only moment it can be. This is how existing hashes move to the current
        // iteration count without a reset.
        var rehashed = verification.RehashNeeded
            ? passwordHasher.HashPassword(new User { Id = account!.Id }, password)
            : null;

        return await CompleteAsync(account!, loginName, rehashed, command, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(string LoginName, PasswordAccount? Account)> FindAccountAsync(
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

    private Task<PasswordAccount?> FindAsync(Expression<Func<User, bool>> match, CancellationToken cancellationToken) =>
        db.Users
          .AsNoTracking()
          .Where(match)
          .Where(u => !u.IsDeleted)
          .Select(u => new PasswordAccount(u.Id, u.PasswordHash, u.LockoutEnd, u.Status, u.UserType, u.SecurityStamp))
          .SingleOrDefaultAsync(cancellationToken);

    private async Task<SignInResult> CompleteAsync(
        PasswordAccount account,
        string loginName,
        string? rehashed,
        SignInCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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

        var session = await sessions
            .StartAsync(account, loginName, preferredInstitutionId: null, command.IpAddress, command.UserAgent, now, cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return session;
    }
}

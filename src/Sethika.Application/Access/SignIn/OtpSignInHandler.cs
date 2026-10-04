using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// Signs a user in with a code sent to their phone instead of a password.
/// </summary>
/// <remarks>
/// Anyone can type any number, so both halves answer a number with no account exactly as they answer
/// one with an account:
///
/// Requesting a code. The cooldown, the hourly cap and a stored code apply to every number, and the
/// request takes a fixed minimum time (<see cref="IOtpService.SendUndisclosedAsync"/>). Only an
/// account that is active, not locked and has a confirmed phone is actually sent the SMS - a code
/// sent to a number nobody confirmed would sign its new holder into someone else's account.
///
/// Signing in. A wrong code, an expired or burned one, and a number with no account are one failure,
/// with the same work behind it: the code is checked whether or not an account was found.
///
/// Success goes through <see cref="SignInHandler"/>'s own completion - the institution rules, the
/// security stamp bound into a new refresh family, the history row - so the two ways in cannot drift
/// apart. It does not reset the password failure count (see <see cref="SignInHandler.CompleteAsync"/>),
/// and a wrong code does not add to it: codes have their own five-guess limit, and counting them
/// against the password would let anyone lock any account by typing its number.
/// </remarks>
public sealed class OtpSignInHandler(
    ISethikaDbContext db,
    IOtpService otp,
    SignInHandler signIn,
    TimeProvider timeProvider)
{
    private const string ActiveStatus = "Active";

    private static class FailureReasons
    {
        public const string UnknownLogin = "UnknownLogin";
        public const string PhoneNotConfirmed = "PhoneNotConfirmed";
        public const string InvalidCode = "InvalidCode";
        public const string LockedOut = "LockedOut";
        public const string AccountNotActive = "AccountNotActive";
    }

    private sealed record Account(
        Guid Id,
        string Status,
        bool PhoneConfirmed,
        DateTimeOffset? LockoutEnd,
        string UserType,
        string SecurityStamp,
        string PreferredLanguage);

    /// <summary>
    /// Sends a sign-in code to <paramref name="phoneNumber"/> if it belongs to an account that may
    /// receive one, and answers the same either way.
    /// </summary>
    /// <param name="phoneNumber">The number in E.164 form.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<OtpSendResult> RequestAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var now = timeProvider.GetUtcNow();
        var account = await FindAsync(phoneNumber, cancellationToken).ConfigureAwait(false);

        // A locked account is not sent a code it would be refused with: the lockout lasts longer than
        // the code, and the SMS is paid for.
        var mayReceive = account is { PhoneConfirmed: true }
            && string.Equals(account.Status, ActiveStatus, StringComparison.Ordinal)
            && !IsLocked(account, now);

        // In the account's own language, not the page's: the SMS is read on the phone, maybe hours
        // after the page that asked for it was closed. A number with no account is sent nothing, so
        // its language is never used.
        return await otp.SendUndisclosedAsync(
                phoneNumber,
                OtpPurpose.Login,
                account?.PreferredLanguage ?? SupportedLanguages.Default,
                deliver: mayReceive,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Checks the code and, if it is right for an account that may sign in, starts a session.</summary>
    public async Task<SignInResult> SignInAsync(OtpSignInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PhoneNumber);

        var now = timeProvider.GetUtcNow();
        var account = await FindAsync(command.PhoneNumber, cancellationToken).ConfigureAwait(false);

        // Checked whether or not there is an account, so an unknown number costs the same and burns
        // its stored code after the same number of guesses.
        var verified = await otp.VerifyAsync(command.PhoneNumber, OtpPurpose.Login, command.Code ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (verified.Outcome == OtpVerifyOutcome.Unavailable)
        {
            return SignInResult.Failed(SignInFailure.Unavailable);
        }

        var failure = (account, verified.IsVerified) switch
        {
            (null, _) => FailureReasons.UnknownLogin,
            ({ PhoneConfirmed: false }, _) => FailureReasons.PhoneNotConfirmed,
            (_, false) => FailureReasons.InvalidCode,
            _ when IsLocked(account, now) => FailureReasons.LockedOut,
            _ when !string.Equals(account.Status, ActiveStatus, StringComparison.Ordinal) => FailureReasons.AccountNotActive,
            _ => null,
        };

        if (failure is not null)
        {
            db.LoginAttempts.Add(SignInHandler.Attempt(
                account?.Id, command.PhoneNumber, failure, command.IpAddress, command.UserAgent, now));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // As password sign-in answers: locked like a wrong code, because only an existing account
            // can be locked; not active only to someone who proved they hold the phone.
            return SignInResult.Failed(failure switch
            {
                FailureReasons.LockedOut => SignInFailure.LockedOut,
                FailureReasons.AccountNotActive => SignInFailure.NotAllowed,
                _ => SignInFailure.InvalidCredentials,
            });
        }

        return await signIn.CompleteAsync(
                new SessionStart(account!.Id, account.UserType, account.SecurityStamp, command.PhoneNumber, command.IpAddress, command.UserAgent),
                resetFailures: false,
                rehashed: null,
                now,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<Account?> FindAsync(string phoneNumber, CancellationToken cancellationToken) =>
        db.Users
          .AsNoTracking()
          .Where(u => u.PhoneNumber == phoneNumber && !u.IsDeleted)
          .Select(u => new Account(u.Id, u.Status, u.PhoneConfirmed, u.LockoutEnd, u.UserType, u.SecurityStamp, u.PreferredLanguage))
          .SingleOrDefaultAsync(cancellationToken);

    private static bool IsLocked(Account account, DateTimeOffset now) =>
        account.LockoutEnd is { } lockoutEnd && lockoutEnd > now;
}

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.PasswordReset;

/// <summary>
/// A forgotten password, reset by an SMS code to the account's own phone: request a code, trade it
/// for a ticket, set the new password with the ticket.
/// </summary>
/// <remarks>
/// The phone number is the account's identity, so the reset goes by SMS. Three properties this
/// exists to keep:
///
/// No account enumeration. Every number gets the same answer to a request. A number that may not
/// reset - unknown, suspended, deleted, unconfirmed, or <c>MfaRequired</c> - gets a decoy instead of
/// an SMS (<see cref="IOtpService.SendDecoyAsync"/>), so its cooldown, its 429 and its answer to a
/// guessed code are the real ones too. Every request then waits out <see cref="RequestFloor"/>, so
/// the SMS hand-off, the one step only a real account takes, does not show in the timing.
///
/// <c>MfaRequired</c> accounts (platform staff) are refused. An SMS alone is one factor, and these
/// are the most powerful accounts; their recovery is the bootstrap command (plan Q5).
///
/// A reset ends every session. The new hash, a rotated stamp with every refresh token revoked, the
/// cleared lockout and an outbox row commit together. The user is not signed in: they sign in with
/// the new password, which proves it was set as they meant it.
/// </remarks>
public sealed partial class PasswordResetHandler(
    ISethikaDbContext db,
    IOtpService otp,
    IPasswordResetTickets tickets,
    IPasswordPolicy passwordPolicy,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    ILogger<PasswordResetHandler> logger)
{
    /// <summary>The shortest a reset request may take to answer, whatever the number.</summary>
    /// <remarks>
    /// Above the console sender's few milliseconds and a typical gateway call. A gateway slower than
    /// this would show through again; the outbox delivering SMS off the request is the real fix.
    /// </remarks>
    public static readonly TimeSpan RequestFloor = TimeSpan.FromSeconds(1);

    /// <summary>How long a ticket waits for the new password: enough to type it twice.</summary>
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Written to <c>auth.RefreshTokens.RevokedReason</c> for the sessions a reset ends.</summary>
    public const string RevokedReason = "PasswordReset";

    private const string ActiveStatus = "Active";

    private sealed record Holder(
        Guid Id,
        string SecurityStamp,
        string PreferredLanguage,
        string PhoneNumber,
        string? Email,
        string FullName);

    /// <summary>
    /// Sends a reset code to <paramref name="phoneNumber"/> if it belongs to an account that may
    /// reset, and answers the same way whether it does or not.
    /// </summary>
    /// <param name="phoneNumber">E.164, already normalised.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<OtpSendResult> RequestAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var started = timeProvider.GetTimestamp();

        var holder = await FindHolderAsync(phoneNumber, cancellationToken).ConfigureAwait(false);

        OtpSendResult sent;
        if (holder is null)
        {
            sent = await otp.SendDecoyAsync(phoneNumber, OtpPurpose.Reset, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                // The message goes in the account's language, not the page's: the account holder
                // reads it, and whoever typed the number may not be them.
                sent = await otp.SendAsync(phoneNumber, OtpPurpose.Reset, holder.PreferredLanguage, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gateway failed after the code was stored. A 500 here, where a number without an
                // account gets 202, would say the number has one. The failure is logged here and in
                // notify.Notifications; the holder sees no SMS and asks again.
                LogSmsFailed(logger, ex);
                sent = OtpSendResult.Sent();
            }
        }

        var remaining = RequestFloor - timeProvider.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    /// <summary>
    /// Checks a reset code and, if it is right, returns a ticket bound to the account and its
    /// current stamp in place of the number-bound ticket the code check issues.
    /// </summary>
    /// <param name="phoneNumber">E.164, already normalised.</param>
    /// <param name="code">The code as the user typed it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<OtpVerifyResult> VerifyAsync(string phoneNumber, string? code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        // The ticket this issues is bound to the number, and the reset request carries no number.
        // It is dropped and expires unused, as step-up drops its own.
        var verified = await otp.VerifyAsync(phoneNumber, OtpPurpose.Reset, code ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (!verified.IsVerified)
        {
            return verified;
        }

        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        // Reached without an account only by guessing a decoy code - five tries at one in a million.
        // The guesser gets a ticket like any other that no reset will accept, rather than an answer
        // that would say the number has no account.
        var holder = await FindHolderAsync(phoneNumber, cancellationToken).ConfigureAwait(false);
        if (holder is null)
        {
            return OtpVerifyResult.Verified(ticket);
        }

        var stored = await tickets.StoreAsync(
            ticket,
            new PasswordResetTicket(holder.Id, holder.SecurityStamp),
            TicketLifetime,
            cancellationToken).ConfigureAwait(false);

        return stored ? OtpVerifyResult.Verified(ticket) : OtpVerifyResult.Unavailable();
    }

    /// <summary>Sets the new password and ends every session, or says why not.</summary>
    public async Task<ResetPasswordResult> ResetAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var presented = command.Ticket ?? string.Empty;

        // Looked at, not spent: a password the policy refuses then costs a correction, not another
        // SMS - the order signup keeps for the same reason.
        var found = await tickets.FindAsync(presented, cancellationToken).ConfigureAwait(false);
        if (found.IsUnavailable)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.Unavailable);
        }

        var holder = found.Ticket is { } issuedFor
            ? await FindHolderAsync(issuedFor, cancellationToken).ConfigureAwait(false)
            : null;

        if (holder is null)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.InvalidTicket);
        }

        var password = command.NewPassword ?? string.Empty;
        var policy = passwordPolicy.Validate(password, [holder.PhoneNumber, holder.Email, holder.FullName]);
        if (!policy.IsValid)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.PasswordRejected, policy.Failures);
        }

        // Spent before anything is written, so whatever fails from here on, the ticket is gone and
        // cannot be presented again. Of two requests racing with one ticket, only one gets past here.
        var redeemed = await tickets.RedeemAsync(presented, cancellationToken).ConfigureAwait(false);
        if (redeemed.IsUnavailable)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.Unavailable);
        }

        if (redeemed.Ticket is not { } spent)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.InvalidTicket);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = passwordHasher.HashPassword(new User { Id = spent.UserId }, password);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Conditional on the stamp the ticket carries and on the account still being one that may
        // reset, in the same statement that writes: a logout everywhere or a suspension that lands
        // between the check above and here still wins.
        var updated = await EligibleAccounts()
            .Where(u => u.Id == spent.UserId && u.SecurityStamp == spent.SecurityStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordChangedAt, now)
                    .SetProperty(u => u.AccessFailedCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            return ResetPasswordResult.Failed(ResetPasswordFailure.InvalidTicket);
        }

        // The first of plan A1's three duties: whoever knew the old password - the reason for many
        // resets - loses every session it opened.
        await SecurityStamps.RotateAsync(db, spent.UserId, RevokedReason, now, cancellationToken).ConfigureAwait(false);

        // The id only, like DonorSignedUp: the consumer that tells the holder their password was
        // changed looks the number up, so the outbox holds no PII.
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "PasswordReset",
            AggregateType = "User",
            AggregateId = spent.UserId.ToString(),
            Payload = JsonSerializer.Serialize(new { userId = spent.UserId }),
            OccurredAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ResetPasswordResult.Success;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A password-reset SMS could not be sent; the request was answered as if it had been.")]
    private static partial void LogSmsFailed(ILogger logger, Exception exception);

    /// <summary>The accounts a reset may touch. One definition for the request, the code and the write.</summary>
    private IQueryable<User> EligibleAccounts() =>
        db.Users.Where(u => !u.IsDeleted && u.Status == ActiveStatus && u.PhoneConfirmed && u.PhoneNumber != null && !u.MfaRequired);

    private Task<Holder?> FindHolderAsync(string phoneNumber, CancellationToken cancellationToken) =>
        EligibleAccounts()
            .AsNoTracking()
            .Where(u => u.PhoneNumber == phoneNumber)
            .Select(u => new Holder(u.Id, u.SecurityStamp, u.PreferredLanguage, u.PhoneNumber!, u.Email, u.FullName))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Holder?> FindHolderAsync(PasswordResetTicket issuedFor, CancellationToken cancellationToken) =>
        EligibleAccounts()
            .AsNoTracking()
            .Where(u => u.Id == issuedFor.UserId && u.SecurityStamp == issuedFor.SecurityStamp)
            .Select(u => new Holder(u.Id, u.SecurityStamp, u.PreferredLanguage, u.PhoneNumber!, u.Email, u.FullName))
            .SingleOrDefaultAsync(cancellationToken);
}

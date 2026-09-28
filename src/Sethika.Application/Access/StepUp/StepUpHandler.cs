using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.StepUp;

/// <summary>
/// MFA step-up by SMS - plan 5.3, A5 and A6. A signed-in user proves again that they hold their
/// phone, and the access token they are using may then exercise <c>RequiresMfa</c> permissions for
/// <see cref="GrantLifetime"/>.
/// </summary>
/// <remarks>
/// The code goes to the phone on the account, never to a number in the request: a step-up proves the
/// account holder is present, and a code sent anywhere else would prove only that someone has a
/// phone. The OTP purpose is <see cref="OtpPurpose.StepUp"/>, so a signup code the attacker had sent
/// to the same number cannot be presented here.
///
/// One step-up covers every sensitive action until the grant expires, not one action. The grant is
/// bound to the access token, so it also ends when that token is refreshed (A5).
/// </remarks>
public sealed class StepUpHandler(
    ISethikaDbContext db,
    IOtpService otp,
    IStepUpGrants grants,
    TimeProvider timeProvider)
{
    /// <summary>How long a step-up lasts (plan A5).</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(15);

    private const string ActiveStatus = "Active";

    private sealed record Holder(string PhoneNumber, string PreferredLanguage);

    /// <summary>Sends a step-up code to the user's own confirmed phone.</summary>
    public async Task<StepUpRequestResult> RequestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (failure, holder) = await FindHolderAsync(userId, cancellationToken).ConfigureAwait(false);
        if (holder is null)
        {
            return StepUpRequestResult.Refused(failure);
        }

        var sent = await otp.SendAsync(holder.PhoneNumber, OtpPurpose.StepUp, holder.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);

        return StepUpRequestResult.Attempted(sent);
    }

    /// <summary>Checks the code and, if it is right, grants step-up to the presenting token.</summary>
    public async Task<StepUpVerifyResult> VerifyAsync(StepUpVerifyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TokenId);

        var (failure, holder) = await FindHolderAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (holder is null)
        {
            return StepUpVerifyResult.Refused(failure);
        }

        // The ticket this also issues exists to carry a verification to a later request (A17). A
        // step-up's proof is the grant below, so the ticket is dropped and expires unused.
        var verified = await otp.VerifyAsync(holder.PhoneNumber, OtpPurpose.StepUp, command.Code ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (!verified.IsVerified)
        {
            return StepUpVerifyResult.Checked(verified.Outcome, verified.AttemptsRemaining);
        }

        // Recorded before the grant is stored, so no grant can exist without its history row. The
        // reverse - a row whose grant failed to store - records a verification that did happen.
        db.LoginAttempts.Add(new LoginAttempt
        {
            UserId = command.UserId,
            LoginName = holder.PhoneNumber,
            Succeeded = true,
            MfaUsed = true,
            IpAddress = Truncate(command.IpAddress, 45),
            UserAgent = Truncate(command.UserAgent, 400),
            AttemptedAt = timeProvider.GetUtcNow().UtcDateTime,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var granted = await grants.GrantAsync(command.UserId, command.TokenId, GrantLifetime, cancellationToken)
            .ConfigureAwait(false);

        return StepUpVerifyResult.Checked(granted ? OtpVerifyOutcome.Verified : OtpVerifyOutcome.Unavailable);
    }

    private async Task<(StepUpFailure Failure, Holder? Holder)> FindHolderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.IsDeleted, u.PhoneNumber, u.PhoneConfirmed, u.PreferredLanguage })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.IsDeleted || !string.Equals(user.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return (StepUpFailure.AccountNotActive, null);
        }

        if (!user.PhoneConfirmed || string.IsNullOrEmpty(user.PhoneNumber))
        {
            return (StepUpFailure.NoVerifiedPhone, null);
        }

        return (StepUpFailure.None, new Holder(user.PhoneNumber, user.PreferredLanguage));
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

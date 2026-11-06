using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Platform;

namespace Sethika.Application.Access.Session;

/// <summary>
/// Refresh-token rotation with reuse detection, institution switching, and logging out - plan 5.2.
/// </summary>
/// <remarks>
/// Every refresh spends the presented token and issues its successor in the same family. A token is
/// therefore only ever valid once, and seeing one presented again after it was spent means two
/// parties hold it: the user and whoever copied it. There is no telling which is which, so the whole
/// family is revoked and both must sign in again - the thief cannot, the user can.
///
/// The one exception is a second presentation within <see cref="RotationGracePeriod"/> of the
/// rotation. Two browser tabs refreshing at the same moment both send the old cookie; revoking the
/// family for that would sign an ordinary user out of everything. The late one is refused as
/// <see cref="SessionFailure.Superseded"/> - it gets no tokens - but the family survives. A copy
/// replayed any later is caught. Superseded is only said while the winner's session is alive; once
/// it has ended, the late request is answered as the ended session it is.
///
/// Rotation is claimed by a conditional UPDATE rather than a read and a write: of any number of
/// requests presenting the same token at once, exactly one finds it still unrevoked. It needs no
/// lock hint, so the handler does not know it runs on SQL Server (plan A16).
/// </remarks>
public sealed class SessionHandler(ISethikaDbContext db, ITokenService tokens, TimeProvider timeProvider, ISupportTickets supportTickets, IStepUpGrants stepUps)
{
    /// <summary>How long after a rotation the spent token is treated as a race, not a theft.</summary>
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(10);

    private const string ActiveStatus = "Active";
    private const string PlatformStaffUserType = "PlatformStaff";

    /// <summary>The values written to <c>auth.RefreshTokens.RevokedReason</c>.</summary>
    public static class RevokedReasons
    {
        public const string Rotated = "Rotated";
        public const string ReuseDetected = "ReuseDetected";
        public const string Logout = "Logout";
        public const string LogoutEverywhere = "LogoutEverywhere";
        public const string PasswordChanged = "PasswordChanged";
        public const string SecurityStampChanged = "SecurityStampChanged";
        public const string AccountNotActive = "AccountNotActive";
    }

    private sealed record Presented(
        long Id,
        Guid UserId,
        Guid FamilyId,
        int? ActiveInstitutionId,
        DateTime ExpiresAt,
        DateTime? RevokedAt,
        string? RevokedReason,
        string SecurityStamp);

    /// <summary>Spends the presented token and issues the next pair, or says why not.</summary>
    public async Task<SessionResult> RefreshAsync(RefreshCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var at = now.UtcDateTime;

        var presented = await FindAsync(command.RefreshToken, cancellationToken).ConfigureAwait(false);
        if (presented is null)
        {
            return SessionResult.Failed(SessionFailure.Invalid);
        }

        if (presented.RevokedAt is { } revokedAt)
        {
            if (string.Equals(presented.RevokedReason, RevokedReasons.Rotated, StringComparison.Ordinal)
                && revokedAt <= at - RotationGracePeriod)
            {
                await RevokeFamilyAsync(presented.FamilyId, RevokedReasons.ReuseDetected, at, cancellationToken).ConfigureAwait(false);
                return SessionResult.Failed(SessionFailure.ReuseDetected);
            }

            return string.Equals(presented.RevokedReason, RevokedReasons.Rotated, StringComparison.Ordinal)
                ? await SupersededOrEndedAsync(presented, at, cancellationToken).ConfigureAwait(false)
                : SessionResult.Failed(SessionFailure.Invalid);
        }

        if (presented.ExpiresAt <= at)
        {
            return SessionResult.Failed(SessionFailure.Invalid);
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == presented.UserId)
            .Select(u => new { u.Status, u.IsDeleted, u.SecurityStamp, u.UserType })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user.IsDeleted || !string.Equals(user.Status, ActiveStatus, StringComparison.Ordinal))
        {
            await RevokeFamilyAsync(presented.FamilyId, RevokedReasons.AccountNotActive, at, cancellationToken).ConfigureAwait(false);
            return SessionResult.Failed(SessionFailure.AccountNotActive);
        }

        // The stamp changes on a password change and on "log out everywhere". A token issued before
        // that is refused here even if the revocation that went with the change missed it - for
        // instance because this very token was being rotated at the same moment.
        if (!string.Equals(user.SecurityStamp, presented.SecurityStamp, StringComparison.Ordinal))
        {
            await RevokeFamilyAsync(presented.FamilyId, RevokedReasons.SecurityStampChanged, at, cancellationToken).ConfigureAwait(false);
            return SessionResult.Failed(SessionFailure.Invalid);
        }

        var institutions = await UserInstitutions.LoadAsync(db, presented.UserId, now, cancellationToken).ConfigureAwait(false);

        InstitutionAccess? active;
        if (command.SwitchToPersonal)
        {
            active = null;
        }
        else if (command.SwitchToInstitution is { } requested)
        {
            active = institutions.FirstOrDefault(i => string.Equals(i.Slug, requested, StringComparison.OrdinalIgnoreCase))
                     // Plan 04, Y1a: platform support into an institution they do not belong to, by
                     // spending the ticket a stepped-up request just issued.
                     ?? await SupportTargetAsync(user.UserType, presented.UserId, requested, null, spendTicket: true, cancellationToken).ConfigureAwait(false);
            if (active is null)
            {
                return SessionResult.Failed(SessionFailure.InstitutionNotAvailable);
            }
        }
        else if (presented.ActiveInstitutionId is { } current)
        {
            // Re-checked, not carried over: a role revoked since the last refresh drops its
            // institution from the new token, and the only one left, if any, takes its place.
            active = institutions.FirstOrDefault(i => i.Id == current)
                     // Still platform support there: the permission is read again, no ticket needed.
                     ?? await SupportTargetAsync(user.UserType, presented.UserId, null, current, spendTicket: false, cancellationToken).ConfigureAwait(false)
                     ?? (institutions.Count == 1 ? institutions[0] : null);
        }
        else
        {
            // A personal session stays personal (plan 04, P1). Sign-in picks the only institution
            // for the person (SessionStarter); once they have left it for their own account, a
            // refresh must not put them back - that is what made a donor's screens unreachable
            // for anyone who belongs to exactly one institution.
            active = null;
        }

        var issued = tokens.Issue(new TokenSubject(
            presented.UserId,
            active?.Id,
            IsPlatform: string.Equals(user.UserType, PlatformStaffUserType, StringComparison.Ordinal)));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var claimed = await db.RefreshTokens
            .Where(t => t.Id == presented.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, at)
                    .SetProperty(t => t.RevokedReason, RevokedReasons.Rotated),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            // Another request presenting the same token rotated it first - or revoked it, a logout
            // at the same instant, which leaves no live session to have been superseded by.
            return await SupersededOrEndedAsync(presented, at, cancellationToken).ConfigureAwait(false);
        }

        var successor = RefreshTokenRows.New(
            issued,
            presented.UserId,
            presented.FamilyId,
            active?.Id,
            user.SecurityStamp,
            command.IpAddress,
            command.UserAgent,
            now);

        db.RefreshTokens.Add(successor);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await db.RefreshTokens
            .Where(t => t.Id == presented.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ReplacedByTokenId, successor.Id), cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Plan 09, M28: a step-up survives the token's rotation only when the request also showed the
        // access token holding it - the same person's, mid-session. A refresh cookie alone carries none.
        if (command.PresentedAccess is { } access && access.UserId == presented.UserId)
        {
            await stepUps.CarryAsync(presented.UserId, access.TokenId, issued.Access.JwtId, cancellationToken).ConfigureAwait(false);
        }

        // A support session's institution is not among the user's own; listed so the client can name it.
        if (active is not null && !institutions.Any(i => i.Id == active.Id))
        {
            institutions.Add(active);
        }

        return SessionResult.Success(issued, institutions, active);
    }

    /// <summary>
    /// The live institution a platform user may act in as support (plan 04, Y1): platform staff,
    /// holding the support permission now, and - when entering - spending a ticket. Null otherwise.
    /// </summary>
    private async Task<InstitutionAccess?> SupportTargetAsync(
        string userType,
        Guid userId,
        string? slug,
        int? id,
        bool spendTicket,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(userType, PlatformStaffUserType, StringComparison.Ordinal))
        {
            return null;
        }

        var target = await db.Institutions
            .AsNoTracking()
            .Where(i => !i.IsDeleted && (i.Status == "Active" || i.Status == "Onboarding"))
            .Where(i => slug != null ? i.Slug == slug : i.Id == id)
            .Select(i => new InstitutionAccess(i.Id, i.Slug, i.Name))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (target is null || !await PlatformSupport.HoldsAsync(db, userId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return !spendTicket || await supportTickets.SpendAsync(userId, target.Id, cancellationToken).ConfigureAwait(false)
            ? target
            : null;
    }

    /// <summary>Ends the session the token belongs to - its whole family. Unknown tokens are ignored.</summary>
    public async Task LogOutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var presented = await FindAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        if (presented is not null)
        {
            await RevokeFamilyAsync(presented.FamilyId, RevokedReasons.Logout, timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends every session the user has, on every device, and rotates their security stamp.
    /// </summary>
    /// <returns>False when the presented token is not a live session, so there is no user to act for.</returns>
    /// <remarks>
    /// Requires a live token, not merely a known one: a copy that reuse detection has already revoked
    /// must not become a way to sign the real user out of everything.
    /// </remarks>
    public async Task<bool> LogOutEverywhereAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var at = timeProvider.GetUtcNow().UtcDateTime;
        var presented = await FindAsync(refreshToken, cancellationToken).ConfigureAwait(false);

        if (presented is null || presented.RevokedAt is not null || presented.ExpiresAt <= at)
        {
            return false;
        }

        var stampMatches = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == presented.UserId && u.SecurityStamp == presented.SecurityStamp, cancellationToken)
            .ConfigureAwait(false);

        if (!stampMatches)
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await SecurityStamps.RotateAsync(db, presented.UserId, RevokedReasons.LogoutEverywhere, at, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private async Task<SessionResult> SupersededOrEndedAsync(Presented presented, DateTime at, CancellationToken cancellationToken)
    {
        var sessionIsAlive = await db.RefreshTokens
            .AsNoTracking()
            .AnyAsync(
                t => t.FamilyId == presented.FamilyId
                     && t.RevokedAt == null
                     && t.ExpiresAt > at
                     && db.Users.Any(u => u.Id == t.UserId && u.SecurityStamp == t.SecurityStamp),
                cancellationToken)
            .ConfigureAwait(false);

        return SessionResult.Failed(sessionIsAlive ? SessionFailure.Superseded : SessionFailure.Invalid);
    }

    private async Task<Presented?> FindAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var hash = tokens.HashRefreshToken(refreshToken);

        return await db.RefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => new Presented(
                t.Id, t.UserId, t.FamilyId, t.ActiveInstitutionId, t.ExpiresAt, t.RevokedAt, t.RevokedReason, t.SecurityStamp))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, string reason, DateTime at, CancellationToken cancellationToken) =>
        db.RefreshTokens
          .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
          .ExecuteUpdateAsync(
              setters => setters.SetProperty(t => t.RevokedAt, at).SetProperty(t => t.RevokedReason, reason),
              cancellationToken);
}

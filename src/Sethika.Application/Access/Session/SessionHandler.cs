using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

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
public sealed class SessionHandler(ISethikaDbContext db, ITokenService tokens, TimeProvider timeProvider)
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
        if (command.SwitchToInstitution is { } requested)
        {
            active = institutions.FirstOrDefault(i => string.Equals(i.Slug, requested, StringComparison.OrdinalIgnoreCase));
            if (active is null)
            {
                return SessionResult.Failed(SessionFailure.InstitutionNotAvailable);
            }
        }
        else
        {
            // Re-checked, not carried over: a role revoked since the last refresh drops its
            // institution from the new token.
            active = institutions.FirstOrDefault(i => i.Id == presented.ActiveInstitutionId)
                     ?? (institutions.Count == 1 ? institutions[0] : null);
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

        return SessionResult.Success(issued, institutions, active);
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

    /// <summary>
    /// Answers a spent token presented within the grace period: <see cref="SessionFailure.Superseded"/>
    /// while the session that won the race is still alive, <see cref="SessionFailure.Invalid"/> once
    /// it has ended.
    /// </summary>
    /// <remarks>
    /// Superseded tells the client "your cookies already hold the successor, retry with them" and is
    /// answered without clearing the cookies. After a logout, a logout everywhere, a stamp change or
    /// the successor's expiry that is false, and the client must hear what every ended session hears.
    ///
    /// "Alive" is the test a refresh applies - not revoked, not expired, stamp unchanged - asked of
    /// the family rather than of the direct successor alone. A family is one chain and only its newest
    /// token is unrevoked, so this is the successor's successor when the winning tab has already
    /// rotated again inside the grace period; asking the direct successor would call that session
    /// ended and have the late tab clear the cookie the winner now holds.
    /// </remarks>
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

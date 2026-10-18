using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Access;

/// <summary>
/// Rotating a user's security stamp - the first of the three duties plan A1 took over from
/// <c>UserManager</c>.
/// </summary>
/// <remarks>
/// Called from every path that must end the user's existing sessions: "log out everywhere",
/// password change, and MFA change when it exists. It does two things, each enough on its own and
/// kept together on purpose. Revoking every live refresh token ends the sessions at once. Changing
/// the stamp catches the one revocation cannot: a refresh already mid-rotation, whose successor is
/// written after the revoke has run - that successor carries the old stamp, and the next refresh
/// refuses it.
///
/// Must run inside the caller's transaction, so the stamp and the revocations commit together.
/// </remarks>
public static class SecurityStamps
{
    /// <summary>Gives <paramref name="userId"/> a new stamp and revokes every live refresh token.</summary>
    /// <returns>
    /// The new stamp, for a caller that issues a session in the same transaction - password change
    /// keeps the calling device signed in, and its new refresh token must carry the stamp it survives.
    /// </returns>
    public static async Task<string> RotateAsync(
        ISethikaDbContext db,
        Guid userId,
        string revokedReason,
        DateTime at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var stamp = Guid.NewGuid().ToString("N");

        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(u => u.SecurityStamp, stamp).SetProperty(u => u.UpdatedAt, at),
                cancellationToken)
            .ConfigureAwait(false);

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, at).SetProperty(t => t.RevokedReason, revokedReason),
                cancellationToken)
            .ConfigureAwait(false);

        return stamp;
    }
}

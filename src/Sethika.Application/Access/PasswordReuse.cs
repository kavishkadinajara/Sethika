using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Access;

/// <summary>
/// Refuses a new password that is one of the user's last <see cref="Remembered"/>, and remembers
/// each one a change or reset replaces, in <c>auth.PasswordHistory</c>.
/// </summary>
/// <remarks>
/// Password change and reset both go through here, so the rule cannot hold for one and not the
/// other. Only hashes are kept - the same PBKDF2 hashes the account held - and each check verifies
/// the candidate against them, so remembering five costs up to five hashes of a request that already
/// pays for one. Both callers are rate-limited for that reason.
/// </remarks>
public static class PasswordReuse
{
    /// <summary>How many passwords are refused: the current one and the four before it.</summary>
    public const int Remembered = 5;

    /// <summary>True when <paramref name="candidate"/> is the current password or a recent one.</summary>
    /// <param name="db">The context.</param>
    /// <param name="hasher">Verifies the candidate against each stored hash.</param>
    /// <param name="userId">Whose passwords.</param>
    /// <param name="currentHash">The account's hash now, or null when the caller has already ruled it out.</param>
    /// <param name="candidate">The new password, as typed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<bool> IsRecentAsync(
        ISethikaDbContext db,
        IPasswordHasher<User> hasher,
        Guid userId,
        string? currentHash,
        string candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);

        var hashes = await db.PreviousPasswords
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(Remembered - 1)
            .Select(p => p.PasswordHash)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (currentHash is not null)
        {
            hashes.Insert(0, currentHash);
        }

        var user = new User { Id = userId };
        return hashes.Exists(hash => hasher.VerifyHashedPassword(user, hash, candidate) != PasswordVerificationResult.Failed);
    }

    /// <summary>
    /// Adds the hash being replaced to the history, for the caller's transaction to save. An account
    /// that had no password has nothing to remember.
    /// </summary>
    public static void Remember(ISethikaDbContext db, Guid userId, string? replacedHash, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (replacedHash is null)
        {
            return;
        }

        db.PreviousPasswords.Add(new PreviousPassword { UserId = userId, PasswordHash = replacedHash, CreatedAt = at });
    }
}

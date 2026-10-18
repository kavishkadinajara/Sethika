using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Access;

/// <summary>An institution a user may act for.</summary>
/// <param name="Id">Internal id, for the token only. Never sent to the client.</param>
/// <param name="Slug">What the client uses to choose it.</param>
/// <param name="Name">For the chooser screen.</param>
public sealed record InstitutionAccess(int Id, string Slug, string Name);

/// <summary>
/// Which institutions a user may act for right now - plan A7. Read at sign-in and again at every
/// refresh.
/// </summary>
/// <remarks>
/// From <c>auth.UserRoles</c>, not <c>org.InstitutionMembers</c>: that table sits behind an RLS
/// filter predicate, and a session with no institution yet would see none of it.
///
/// Re-read on every refresh rather than trusted from the refresh token row, so a role revoked or
/// expired since takes its institution with it within one access-token lifetime.
/// </remarks>
public static class UserInstitutions
{
    /// <summary>The institutions <paramref name="userId"/> holds an active, in-window role in.</summary>
    public static async Task<List<InstitutionAccess>> LoadAsync(
        ISethikaDbContext db,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var at = now.UtcDateTime;

        var rows = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId
                         && ur.IsActive
                         && ur.Role.IsActive
                         && ur.InstitutionId != null
                         && (ur.ValidFrom == null || ur.ValidFrom <= at)
                         && (ur.ValidTo == null || ur.ValidTo > at))
            .Join(
                db.Institutions.Where(i => !i.IsDeleted && (i.Status == "Active" || i.Status == "Onboarding")),
                ur => ur.InstitutionId,
                i => (int?)i.Id,
                (ur, i) => new { i.Id, i.Slug, i.Name })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new InstitutionAccess(r.Id, r.Slug, r.Name))];
    }
}

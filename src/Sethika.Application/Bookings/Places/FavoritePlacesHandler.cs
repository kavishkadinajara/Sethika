using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;

namespace Sethika.Application.Bookings.Places;

/// <summary>
/// The heart on an institution's page (plan 07, H12): a signed-in person keeps a place among "My
/// places", or lets it go. Both are idempotent, so a double tap or a retry changes nothing.
/// </summary>
/// <remarks>
/// Row-Level Security (<c>sec.fn_Owner</c>) shows and lets a person write only their own rows; the
/// user id comes from the token, never the request.
/// </remarks>
public sealed class FavoritePlacesHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    /// <summary>No active institution by that slug.</summary>
    public const string NotFound = "institution_not_found";

    /// <summary>
    /// Keeps the active institution <paramref name="slug"/> among <paramref name="userId"/>'s places.
    /// <see langword="null"/> on success, also when it was kept already; else <see cref="NotFound"/>.
    /// </summary>
    /// <remarks>
    /// Two taps at once both find no row and both insert; the key refuses the second, which is the
    /// answer the first already gave. The key is the whole guarantee: the look before the insert only
    /// spares a tap on a place already kept a refused insert and its logged error (mutant P3 survives).
    /// </remarks>
    public async Task<string?> AddAsync(Guid userId, string slug, CancellationToken cancellationToken = default)
    {
        var institutionId = await db.Institutions
            .Where(i => i.Slug == slug && i.Status == "Active" && !i.IsDeleted)
            .Select(i => (int?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (institutionId is not { } id)
        {
            return NotFound;
        }

        if (await db.FavoriteInstitutions.AnyAsync(f => f.UserId == userId && f.InstitutionId == id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var favorite = new FavoriteInstitution { UserId = userId, InstitutionId = id, AddedAt = timeProvider.GetUtcNow().UtcDateTime };
        db.FavoriteInstitutions.Add(favorite);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            db.ChangeTracker.Clear();
        }

        return null;
    }

    /// <summary>Lets <paramref name="slug"/> go from <paramref name="userId"/>'s places, if it was there.</summary>
    /// <remarks>By slug whatever the institution's status, so a place that closed can still be let go.</remarks>
    public async Task RemoveAsync(Guid userId, string slug, CancellationToken cancellationToken = default) =>
        await db.FavoriteInstitutions
            .Where(f => f.UserId == userId && db.Institutions.Any(i => i.Id == f.InstitutionId && i.Slug == slug))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
}

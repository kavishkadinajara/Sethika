using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;

namespace Sethika.Application.Access.Language;

/// <summary>
/// Stores the language a user wants Sethika to speak to them in: the web app's locale and every
/// SMS, the one-time codes included (plan 5.6, A11).
/// </summary>
/// <remarks>
/// Only <c>PreferredLanguage</c> and the update stamp are written, through a tracked entity, so the
/// UPDATE carries the row's <c>RowVersion</c>. A concurrent write - a lockout count, a sign-in - makes
/// it fail rather than be overwritten. The caller sent no version (a language choice is not an edit
/// of something they were shown), so a conflict is retried once against the fresh row; a second one
/// is reported, not forced.
/// </remarks>
public sealed class PreferredLanguageHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    private const int Attempts = 2;

    /// <summary>Sets <paramref name="userId"/>'s preferred language. Idempotent.</summary>
    public async Task<PreferredLanguageResult> SetAsync(Guid userId, string? language, CancellationToken cancellationToken = default)
    {
        // Resolve treats blank as "the default", which suits a request that may omit a language.
        // Here blank is a mistake, not a choice of English.
        var resolved = string.IsNullOrWhiteSpace(language) ? null : SupportedLanguages.Resolve(language);
        if (resolved is null)
        {
            return PreferredLanguageResult.UnsupportedLanguage;
        }

        for (var attempt = 1; ; attempt++)
        {
            var user = await db.Users
                .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return PreferredLanguageResult.UserNotFound;
            }

            // Same language: no write, so a repeated call does not churn RowVersion or UpdatedAt.
            if (string.Equals(user.PreferredLanguage, resolved, StringComparison.Ordinal))
            {
                return PreferredLanguageResult.Saved;
            }

            user.PreferredLanguage = resolved;
            user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            user.UpdatedBy = userId;

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return PreferredLanguageResult.Saved;
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                return PreferredLanguageResult.Conflict;
            }
        }
    }
}

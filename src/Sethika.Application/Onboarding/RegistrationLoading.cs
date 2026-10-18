using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Onboarding;

namespace Sethika.Application.Onboarding;

/// <summary>Loads an application with everything the aggregate needs to decide.</summary>
public static class RegistrationLoading
{
    /// <summary>
    /// The application and its five collections, one query each. A single query with five
    /// <c>Include</c>s returns their cartesian product; split queries are a relational-only feature,
    /// and Application depends on EF Core alone (plan 01, A16). Inside the caller's transaction and
    /// under its locks, the six reads see one consistent application.
    /// </summary>
    /// <returns>The application, or <see langword="null"/> if there is none the caller may see.</returns>
    public static async Task<RegistrationRequest?> LoadRegistrationAsync(
        this ISethikaDbContext db,
        string requestNo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var request = await db.RegistrationRequests
            .SingleOrDefaultAsync(r => r.RequestNo == requestNo, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return null;
        }

        var entry = db.Entry(request);
        await entry.Collection(r => r.Documents).LoadAsync(cancellationToken).ConfigureAwait(false);
        await entry.Collection(r => r.OfficeBearers).LoadAsync(cancellationToken).ConfigureAwait(false);
        await entry.Collection(r => r.ChecklistEntries).LoadAsync(cancellationToken).ConfigureAwait(false);
        await entry.Collection(r => r.Clarifications).LoadAsync(cancellationToken).ConfigureAwait(false);
        await entry.Collection(r => r.StatusHistory).LoadAsync(cancellationToken).ConfigureAwait(false);

        return request;
    }
}

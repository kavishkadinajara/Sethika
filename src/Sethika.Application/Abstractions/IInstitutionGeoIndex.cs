using Sethika.Domain.Institutions;

namespace Sethika.Application.Abstractions;

/// <summary>
/// Where the institutions a donor may find are (plan 06, G4): the fast path of a search near a
/// point. An index of SQL Server, never the truth (G5).
/// </summary>
/// <remarks>
/// Holds only institutions that are <c>Active</c> and publicly listed. It can lag the database by an
/// outbox run, so a search still filters what it returns against SQL; it can also be missing or
/// unreachable, and then a search asks SQL Server instead.
/// </remarks>
public interface IInstitutionGeoIndex
{
    /// <summary>
    /// The ids of the institutions within <paramref name="radiusKm"/> of <paramref name="centre"/>, nearest
    /// first; <see langword="null"/> if the index cannot answer (unreachable, or not built yet).
    /// </summary>
    Task<IReadOnlyList<int>?> WithinAsync(GeoPoint centre, double radiusKm, CancellationToken cancellationToken = default);

    /// <summary>Replaces the whole index with what SQL Server holds now. Throws if the index cannot be written.</summary>
    /// <returns>How many institutions the index holds.</returns>
    Task<int> RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts one institution where SQL Server says it is now: in the index at its address if it is
    /// <c>Active</c> and listed, out of it otherwise. Throws if the index cannot be written.
    /// </summary>
    Task RefreshAsync(int institutionId, CancellationToken cancellationToken = default);
}

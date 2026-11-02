using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's donor book (plan 08, K1).</summary>
/// <remarks>
/// Row-Level Security already keeps the donor book to its institution (<c>sec.fn_Tenant</c>); the query
/// names the institution all the same, so a platform connection, which sees every row, still reads one.
/// </remarks>
public sealed class DonorBookQueries(IDbConnectionFactory connections) : IDonorBookQueries
{
    private const string DonorsSql =
        """
        SELECT d.PublicId AS PublicId, d.FullName AS FullName, d.Phone AS Phone, d.Notes AS Notes,
               d.Language AS Language, CAST(CASE WHEN d.UserId IS NULL THEN 0 ELSE 1 END AS BIT) AS HasAccount,
               d.TextConsentAt AS TextConsentAt, d.IsActive AS IsActive,
               (SELECT COUNT(*) FROM booking.StandingSponsorships AS s
                WHERE s.InstitutionDonorId = d.Id AND s.InstitutionId = d.InstitutionId AND s.Status = 'Active') AS ActiveSponsorships,
               d.RowVersion AS Version
        FROM org.InstitutionDonors AS d
        WHERE d.InstitutionId = @institutionId
        ORDER BY d.IsActive DESC, d.FullName, d.Id;
        """;

    public async Task<IReadOnlyList<DonorView>> ListDonorsAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<DonorRow>(new CommandDefinition(
            DonorsSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new DonorView(
            r.PublicId, r.FullName, r.Phone, r.Notes, r.Language, r.HasAccount,
            r.TextConsentAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            r.IsActive, r.ActiveSponsorships, r.Version))];
    }

    private sealed class DonorRow
    {
        public Guid PublicId { get; init; }
        public string FullName { get; init; } = null!;
        public string Phone { get; init; } = null!;
        public string? Notes { get; init; }
        public string Language { get; init; } = null!;
        public bool HasAccount { get; init; }
        public DateTime? TextConsentAt { get; init; }
        public bool IsActive { get; init; }
        public int ActiveSponsorships { get; init; }
        public byte[] Version { get; init; } = [];
    }
}

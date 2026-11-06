using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Queries;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IOnlineGiving"/>: whether an institution has an enabled merchant, read as the platform
/// (plan 09, M10).
/// </summary>
/// <remarks>
/// An institution's public page has no session, and <c>giving.PaymentMerchants</c> answers only to its own
/// institution. Like <see cref="SqlStandingHolds"/>, the connection is stamped as the platform here, never
/// from the request, and the one statement returns a yes or a no: no merchant id, nothing of the secret.
/// It is asked only about an institution the public read already found active.
/// </remarks>
public sealed class SqlOnlineGiving(string connectionString) : IOnlineGiving
{
    private const string IsOnSql =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM giving.PaymentMerchants AS m
            JOIN org.Institutions AS i ON i.Id = m.InstitutionId
            WHERE i.Slug = @slug AND m.IsEnabled = 1
        ) THEN 1 ELSE 0 END AS BIT);
        """;

    private readonly SqlConnectionFactory _platform = new(connectionString, PlatformJobUser.Instance);

    public async Task<bool> IsOnAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            IsOnSql,
            new { slug },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper read of the caller's own account, per the reads-use-Dapper rule.</summary>
public sealed class AccountQueries(IDbConnectionFactory connections) : IAccountQueries
{
    private const string ProfileSql =
        """
        SELECT u.FullName          AS FullName,
               u.DisplayName       AS DisplayName,
               u.PreferredLanguage AS PreferredLanguage
        FROM auth.Users AS u
        WHERE u.Id = @userId
          AND u.IsDeleted = 0;
        """;

    public async Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<AccountProfile>(new CommandDefinition(
            ProfileSql,
            new { userId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

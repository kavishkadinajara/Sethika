using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;
using Sethika.Domain.Institutions;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Caching;

/// <summary>Where the geo index lives. Section <c>GeoIndex</c>.</summary>
public sealed class GeoIndexOptions
{
    public const string SectionName = "GeoIndex";

    /// <summary>
    /// The Redis key of the index (plan 06, G4). One key holds every institution, so the tests give
    /// each host its own and never touch the one a running API keeps in the same database.
    /// </summary>
    public string Key { get; set; } = "geo:institutions";
}

/// <summary>
/// The geo index as one Redis GEO set (plan 06, G4): members are institution ids, positions their
/// addresses, only <c>Active</c> and publicly listed institutions.
/// </summary>
/// <remarks>
/// <para>
/// <c>GEORADIUS</c>, not <c>GEOSEARCH</c>: the local Redis is 3.2, which has no <c>GEOSEARCH</c>
/// (6.2), and managed Redis still answers <c>GEORADIUS</c>.
/// </para>
/// <para>
/// A missing key means an index not built, not a circle with nobody in it: <see cref="WithinAsync"/>
/// then answers <see langword="null"/> and the search asks SQL Server (G5). So nothing may create the
/// key except a full rebuild: a refresh that finds no index rebuilds it, rather than starting one
/// with a single member that would hide every other institution.
/// </para>
/// </remarks>
public sealed partial class RedisInstitutionGeoIndex(
    IConnectionMultiplexer redis,
    IDbConnectionFactory connections,
    IOptions<GeoIndexOptions> options,
    ILogger<RedisInstitutionGeoIndex> logger) : IInstitutionGeoIndex
{
    private const string ListedSql =
        """
        SELECT i.Id AS Id, i.Latitude AS Latitude, i.Longitude AS Longitude
        FROM org.Institutions AS i
        WHERE i.Status = 'Active' AND i.IsPubliclyListed = 1 AND i.IsDeleted = 0;
        """;

    private const string OneSql =
        """
        SELECT i.Id AS Id, i.Latitude AS Latitude, i.Longitude AS Longitude
        FROM org.Institutions AS i
        WHERE i.Id = @institutionId AND i.Status = 'Active' AND i.IsPubliclyListed = 1 AND i.IsDeleted = 0;
        """;

    private RedisKey Key => options.Value.Key;

    public async Task<IReadOnlyList<int>?> WithinAsync(GeoPoint centre, double radiusKm, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = redis.GetDatabase();
            if (!await database.KeyExistsAsync(Key).ConfigureAwait(false))
            {
                LogNotBuilt(logger, options.Value.Key);
                return null;
            }

            var found = await database
                .GeoRadiusAsync(Key, centre.Longitude, centre.Latitude, radiusKm, GeoUnit.Kilometers, order: Order.Ascending, options: GeoRadiusOptions.None)
                .ConfigureAwait(false);

            return [.. found.Select(r => int.Parse(r.Member.ToString(), NumberStyles.None, CultureInfo.InvariantCulture))];
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogUnavailable(logger, options.Value.Key, ex);
            return null;
        }
    }

    public async Task<int> RebuildAsync(CancellationToken cancellationToken = default)
    {
        List<Position> listed;
        await using (var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            listed = [.. await connection.QueryAsync<Position>(new CommandDefinition(ListedSql, cancellationToken: cancellationToken)).ConfigureAwait(false)];
        }

        var database = redis.GetDatabase();
        if (listed.Count == 0)
        {
            await database.KeyDeleteAsync(Key).ConfigureAwait(false);
            return 0;
        }

        // MULTI/EXEC: a search sees the old index or the new one, never the moment between.
        var transaction = database.CreateTransaction();
        _ = transaction.KeyDeleteAsync(Key);
        _ = transaction.GeoAddAsync(Key, [.. listed.Select(Entry)]);

        if (!await transaction.ExecuteAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Redis did not run the rebuild of {options.Value.Key}.");
        }

        return listed.Count;
    }

    public async Task RefreshAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        Position? position;
        await using (var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            position = await connection.QuerySingleOrDefaultAsync<Position>(new CommandDefinition(
                OneSql,
                new { institutionId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var database = redis.GetDatabase();
        if (!await database.KeyExistsAsync(Key).ConfigureAwait(false))
        {
            await RebuildAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (position is null)
        {
            await database.GeoRemoveAsync(Key, Member(institutionId)).ConfigureAwait(false);
        }
        else
        {
            await database.GeoAddAsync(Key, Entry(position)).ConfigureAwait(false);
        }
    }

    private static GeoEntry Entry(Position p) => new((double)p.Longitude, (double)p.Latitude, Member(p.Id));

    private static RedisValue Member(int institutionId) => institutionId.ToString(CultureInfo.InvariantCulture);

    private sealed class Position
    {
        public int Id { get; init; }
        public decimal Latitude { get; init; }
        public decimal Longitude { get; init; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo index {Key} is not built; searching SQL Server instead.")]
    private static partial void LogNotBuilt(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo index {Key} unavailable; searching SQL Server instead.")]
    private static partial void LogUnavailable(ILogger logger, string key, Exception exception);
}

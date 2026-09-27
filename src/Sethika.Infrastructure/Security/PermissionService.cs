using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using StackExchange.Redis;

namespace Sethika.Infrastructure.Security;

public sealed partial class PermissionService(
    IDbConnectionFactory connectionFactory,
    IConnectionMultiplexer redis,
    ILogger<PermissionService> logger) : IPermissionService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    private const string VersionSql =
        """
        SELECT
            (SELECT u.PermissionsVersion FROM auth.Users u WHERE u.Id = @userId)              AS UserVersion,
            (SELECT i.PermissionsVersion FROM org.Institutions i WHERE i.Id = @institutionId) AS InstitutionVersion;
        """;

    private const string PermissionSql =
        """
        SELECT ep.PermissionCode, ep.RequiresMfa
        FROM auth.vw_EffectivePermissions ep
        WHERE ep.UserId = @userId
          AND ((@institutionId IS NULL AND ep.InstitutionId IS NULL)
               OR ep.InstitutionId = @institutionId);
        """;

    public async Task<PermissionSet> GetEffectivePermissionsAsync(
        Guid userId,
        int? institutionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var (userVersion, institutionVersion) =
            await ReadVersionsAsync(connection, userId, institutionId, cancellationToken).ConfigureAwait(false);

        var key = PermissionCacheKey.For(userId, institutionId, userVersion, institutionVersion);

        var cached = await TryReadCacheAsync(key).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var loaded = await LoadFromViewAsync(connection, userId, institutionId, cancellationToken)
            .ConfigureAwait(false);

        await TryWriteCacheAsync(key, loaded).ConfigureAwait(false);

        return loaded;
    }

    private static async Task<(int UserVersion, int InstitutionVersion)> ReadVersionsAsync(
        IDbConnection connection,
        Guid userId,
        int? institutionId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            VersionSql,
            new { userId, institutionId },
            cancellationToken: cancellationToken);

        var row = await connection
            .QuerySingleAsync<VersionRow>(command)
            .ConfigureAwait(false);

        return (row.UserVersion ?? 0, row.InstitutionVersion ?? 0);
    }

    private static async Task<PermissionSet> LoadFromViewAsync(
        IDbConnection connection,
        Guid userId,
        int? institutionId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            PermissionSql,
            new { userId, institutionId },
            cancellationToken: cancellationToken);

        var rows = (await connection
            .QueryAsync<PermissionRow>(command)
            .ConfigureAwait(false)).ToArray();

        return new PermissionSet(
            rows.Select(r => r.PermissionCode),
            rows.Where(r => r.RequiresMfa).Select(r => r.PermissionCode));
    }

    private async Task<PermissionSet?> TryReadCacheAsync(string key)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<CachedPermissions>((string)value!);

            return payload is null
                ? null
                : new PermissionSet(payload.Codes, payload.MfaCodes);
        }
        catch (Exception ex) when (ex is RedisException or JsonException or TimeoutException)
        {
            LogCacheReadFailed(logger, key, ex);
            return null;
        }
    }

    private async Task TryWriteCacheAsync(string key, PermissionSet permissions)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new CachedPermissions
            {
                Codes = [.. permissions.Codes],
                MfaCodes = [.. permissions.Codes.Where(permissions.RequiresMfa)],
            });

            await redis.GetDatabase()
                .StringSetAsync(key, payload, CacheLifetime)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogCacheWriteFailed(logger, key, ex);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Permission cache read failed for {Key}; falling back to SQL.")]
    private static partial void LogCacheReadFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Permission cache write failed for {Key}; continuing uncached.")]
    private static partial void LogCacheWriteFailed(ILogger logger, string key, Exception exception);

    private sealed class VersionRow
    {
        public int? UserVersion { get; init; }
        public int? InstitutionVersion { get; init; }
    }

    private sealed class PermissionRow
    {
        public string PermissionCode { get; init; } = string.Empty;
        public bool RequiresMfa { get; init; }
    }

    private sealed class CachedPermissions
    {
        public string[] Codes { get; init; } = [];
        public string[] MfaCodes { get; init; } = [];
    }
}

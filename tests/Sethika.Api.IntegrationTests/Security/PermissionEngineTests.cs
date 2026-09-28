using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Integration tests for the permission engine, against the real view and real Redis.
/// </summary>
/// <remarks>
/// Deny-wins and module-disabled are tested here rather than as unit tests on purpose. Both
/// rules are implemented by auth.vw_EffectivePermissions; a unit test could only cover them
/// by reimplementing them in C#, which would mean two sources of truth that can disagree -
/// and the database is the one that actually decides access. Testing the view is testing the
/// thing that enforces the rule.
///
/// Each test reseeds first, because they mutate shared rows and xunit does not guarantee
/// order within a class.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class PermissionEngineTests(SethikaTestDatabase database)
    : IAsyncLifetime
{
    private const string View = "BOOKINGS.CALENDAR.VIEW";
    private const string Approve = "BOOKINGS.CALENDAR.APPROVE";

    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        var options = ConfigurationOptions.Parse(database.RedisConnectionString);
        options.AbortOnConnectFail = false;
        _redis = await ConnectionMultiplexer.ConnectAsync(options);

        await WaitForRedisAsync();
        await database.SeedAsync();
    }

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    /// <summary>
    /// Waits for a real round trip rather than checking IsConnected.
    /// </summary>
    /// <remarks>
    /// With AbortOnConnectFail = false, ConnectAsync returns a multiplexer that may still be
    /// establishing its connection, so IsConnected can be false for a short window even
    /// though Redis is perfectly healthy. Asserting on it made this fixture intermittently
    /// fail every test in the class at once. PING forces the round trip and gives a real
    /// answer, and a genuinely absent Redis still fails - just with a clear message.
    /// </remarks>
    private async Task WaitForRedisAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await _redis.GetDatabase().PingAsync();
                return;
            }
            catch (RedisException ex)
            {
                last = ex;
                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException(
            $"Redis did not answer PING at {database.RedisConnectionString} within 10s. The " +
            "permission cache cannot be faked; start Redis or set SETHIKA_TEST_REDIS.", last);
    }

    private PermissionService ServiceFor(Guid userId, int? institutionId)
    {
        var user = new FixedUser { UserId = userId, InstitutionId = institutionId };
        var factory = new SqlConnectionFactory(database.ConnectionString, user);

        return new PermissionService(factory, _redis, NullLogger<PermissionService>.Instance);
    }

    private async Task<PermissionSet> PermissionsForAlphaAsync() =>
        await ServiceFor(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId)
            .GetEffectivePermissionsAsync(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId);

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>
    /// Production bumps PermissionsVersion on any permission change; the tests below do the
    /// same, because without it the cached entry is still the live one.
    /// </summary>
    private Task BumpUserVersionAsync() => ExecuteAsync(
        "UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @id",
        new { id = SethikaTestDatabase.AlphaUserId });

    private Task BumpInstitutionVersionAsync() => ExecuteAsync(
        "UPDATE org.Institutions SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @id",
        new { id = database.AlphaInstitutionId });

    [Fact]
    public async Task Loads_role_granted_permissions_from_the_view()
    {
        var permissions = await PermissionsForAlphaAsync();

        Assert.True(permissions.Allows(View));
        Assert.True(permissions.Allows(Approve));
    }

    /// <summary>RequiresMfa has to survive the round trip through Redis, not just SQL.</summary>
    [Fact]
    public async Task Carries_the_requires_mfa_flag_through_the_cache()
    {
        await PermissionsForAlphaAsync();          // populate
        var cached = await PermissionsForAlphaAsync();  // read back out of Redis

        Assert.True(cached.RequiresMfa(Approve));
        Assert.False(cached.RequiresMfa(View));
    }

    /// <summary>
    /// Deny wins: the user's role still grants APPROVE, and a deny override must beat it.
    /// </summary>
    [Fact]
    public async Task Deny_override_beats_a_role_grant()
    {
        Assert.True((await PermissionsForAlphaAsync()).Allows(Approve));

        await ExecuteAsync(
            """
            INSERT auth.UserPermissionOverrides
                (UserId, InstitutionId, PermissionId, Effect, Reason, CreatedBy)
            SELECT @userId, @institutionId, p.Id, 'Deny', N'test: deny wins', @userId
            FROM auth.Permissions p WHERE p.Code = @code;
            """,
            new
            {
                userId = SethikaTestDatabase.AlphaUserId,
                institutionId = database.AlphaInstitutionId,
                code = Approve,
            });

        await BumpUserVersionAsync();

        var after = await PermissionsForAlphaAsync();

        Assert.False(after.Allows(Approve));
        Assert.True(after.Allows(View));
    }

    /// <summary>
    /// A deny must beat a grant override too, not just a role grant - otherwise granting
    /// someone an exception would quietly override an explicit revocation.
    /// </summary>
    [Fact]
    public async Task Deny_override_beats_a_grant_override()
    {
        await ExecuteAsync(
            """
            DELETE FROM auth.RolePermissions
            WHERE PermissionId IN (SELECT Id FROM auth.Permissions WHERE Code = @code);

            INSERT auth.UserPermissionOverrides
                (UserId, InstitutionId, PermissionId, Effect, Reason, CreatedBy)
            SELECT @userId, @institutionId, p.Id, 'Grant', N'test: grant', @userId
            FROM auth.Permissions p WHERE p.Code = @code;
            """,
            new
            {
                userId = SethikaTestDatabase.AlphaUserId,
                institutionId = database.AlphaInstitutionId,
                code = Approve,
            });
        await BumpUserVersionAsync();

        Assert.True((await PermissionsForAlphaAsync()).Allows(Approve));

        // now deny the same permission; UX_UPO is unique per (user, institution, permission)
        await ExecuteAsync(
            """
            UPDATE auth.UserPermissionOverrides SET Effect = 'Deny'
            WHERE UserId = @userId
              AND PermissionId IN (SELECT Id FROM auth.Permissions WHERE Code = @code);
            """,
            new { userId = SethikaTestDatabase.AlphaUserId, code = Approve });
        await BumpUserVersionAsync();

        Assert.False((await PermissionsForAlphaAsync()).Allows(Approve));
    }

    /// <summary>
    /// Effective permissions are role grants INTERSECTED with the institution's enabled
    /// modules, so disabling the module must strip every permission under it even though the
    /// role still grants them.
    /// </summary>
    [Fact]
    public async Task Disabling_a_module_strips_its_permissions()
    {
        Assert.True((await PermissionsForAlphaAsync()).Allows(View));

        await ExecuteAsync(
            """
            UPDATE org.InstitutionModules SET IsEnabled = 0
            WHERE InstitutionId = @institutionId
              AND ModuleId IN (SELECT Id FROM auth.Modules WHERE Code = 'BOOKINGS');
            """,
            new { institutionId = database.AlphaInstitutionId });

        await BumpInstitutionVersionAsync();

        var after = await PermissionsForAlphaAsync();

        Assert.False(after.Allows(View));
        Assert.False(after.Allows(Approve));
        Assert.Empty(after.Codes);
    }

    /// <summary>
    /// Re-enabling restores them, which shows the module gate is evaluated each load rather
    /// than baked in permanently at first read.
    /// </summary>
    [Fact]
    public async Task Re_enabling_a_module_restores_its_permissions()
    {
        await ExecuteAsync(
            """
            UPDATE org.InstitutionModules SET IsEnabled = 0
            WHERE InstitutionId = @institutionId;
            """,
            new { institutionId = database.AlphaInstitutionId });
        await BumpInstitutionVersionAsync();
        Assert.Empty((await PermissionsForAlphaAsync()).Codes);

        await ExecuteAsync(
            """
            UPDATE org.InstitutionModules SET IsEnabled = 1
            WHERE InstitutionId = @institutionId;
            """,
            new { institutionId = database.AlphaInstitutionId });
        await BumpInstitutionVersionAsync();

        Assert.True((await PermissionsForAlphaAsync()).Allows(View));
    }

    /// <summary>
    /// The cache-invalidation contract, in one test: the entry really is served from Redis,
    /// and bumping PermissionsVersion is what makes a change visible.
    /// </summary>
    [Fact]
    public async Task Bumping_permissions_version_invalidates_the_cached_entry()
    {
        var before = await PermissionsForAlphaAsync();
        Assert.True(before.Allows(Approve));

        // the key is version-stamped, so it must now exist in Redis
        var versions = await ReadVersionsAsync();
        var key = PermissionCacheKey.For(
            SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId,
            versions.UserVersion, versions.InstitutionVersion);

        Assert.True(await _redis.GetDatabase().KeyExistsAsync(key),
            $"expected the permission set to be cached under {key}");

        // change the data WITHOUT bumping: the stale cached entry must still be served,
        // which is what proves the read is genuinely cached and not hitting SQL each time
        await ExecuteAsync(
            """
            DELETE FROM auth.RolePermissions
            WHERE PermissionId IN (SELECT Id FROM auth.Permissions WHERE Code = @code);
            """,
            new { code = Approve });

        Assert.True((await PermissionsForAlphaAsync()).Allows(Approve));

        // now bump: a different key, a miss, and a reload
        await BumpUserVersionAsync();

        var after = await PermissionsForAlphaAsync();
        Assert.False(after.Allows(Approve));
        Assert.True(after.Allows(View));

        var bumped = await ReadVersionsAsync();
        Assert.NotEqual(versions.UserVersion, bumped.UserVersion);
        Assert.NotEqual(key, PermissionCacheKey.For(
            SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId,
            bumped.UserVersion, bumped.InstitutionVersion));
    }

    /// <summary>A user with no roles anywhere gets an empty set, not everything.</summary>
    [Fact]
    public async Task A_user_with_no_roles_holds_nothing()
    {
        var permissions = await ServiceFor(SethikaTestDatabase.BetaUserId, database.BetaInstitutionId)
            .GetEffectivePermissionsAsync(SethikaTestDatabase.BetaUserId, database.BetaInstitutionId);

        Assert.Empty(permissions.Codes);
    }

    /// <summary>
    /// Alpha's permissions are scoped to Alpha's institution; asking in Beta's context must
    /// not carry them across.
    /// </summary>
    [Fact]
    public async Task Permissions_do_not_leak_across_institutions()
    {
        var inBeta = await ServiceFor(SethikaTestDatabase.AlphaUserId, database.BetaInstitutionId)
            .GetEffectivePermissionsAsync(SethikaTestDatabase.AlphaUserId, database.BetaInstitutionId);

        Assert.Empty(inBeta.Codes);
    }

    private async Task<(int UserVersion, int InstitutionVersion)> ReadVersionsAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        return await connection.QuerySingleAsync<(int, int)>(
            """
            SELECT
                (SELECT PermissionsVersion FROM auth.Users WHERE Id = @userId),
                (SELECT PermissionsVersion FROM org.Institutions WHERE Id = @institutionId);
            """,
            new { userId = SethikaTestDatabase.AlphaUserId, institutionId = database.AlphaInstitutionId });
    }

    private sealed class FixedUser : ICurrentUserContext
    {
        public Guid? UserId { get; init; }
        public int? InstitutionId { get; init; }
        public bool IsPlatform { get; init; }
        public string? TokenId { get; init; }
    }
}

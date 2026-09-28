using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Level ceilings (plan A15) and the RBAC catalogue seed, against the published database.
/// </summary>
/// <remarks>
/// Everything that touches reference data runs in a transaction that is rolled back: ceilings and
/// template grants come from the dacpac, and the fixture's reset deliberately does not restore them.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RbacCatalogueTests(SethikaTestDatabase database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> BeginAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return (connection, (SqlTransaction)await connection.BeginTransactionAsync());
    }

    private static async Task<List<string>> AlphaCodesAsync(SqlConnection connection, SqlTransaction transaction, int institutionId) =>
        [.. await connection.QueryAsync<string>(
            "SELECT PermissionCode FROM auth.vw_EffectivePermissions WHERE UserId = @userId AND InstitutionId = @institutionId;",
            new { userId = SethikaTestDatabase.AlphaUserId, institutionId },
            transaction)];

    private static string CatalogueSeed()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("10_RbacCatalogue.sql", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("10_RbacCatalogue.sql is not embedded in the test assembly.");

        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ the ceiling in the view

    [Fact]
    public async Task AGrantOutsideTheLevelsCeilingIsNotEffective()
    {
        // The steward is at STAFF. ACCESS.ROLES.VIEW is inside STAFF's ceiling, ACCESS.ROLES.CREATE
        // is not; both are granted by hand, as a buggy screen or a script might, with the module on.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            await connection.ExecuteAsync(
                """
                INSERT auth.RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id FROM auth.Roles r CROSS JOIN auth.Permissions p
                WHERE r.Code = 'ALPHA_STEWARD' AND (p.Code = 'ACCESS.ROLES.VIEW' OR p.Code = 'ACCESS.ROLES.CREATE');

                INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
                SELECT @institutionId, m.Id, 1, 'Manual', @userId FROM auth.Modules m WHERE m.Code = 'ACCESS';
                """,
                new { institutionId = database.AlphaInstitutionId, userId = SethikaTestDatabase.AlphaUserId },
                transaction);

            var codes = await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId);

            Assert.Contains("ACCESS.ROLES.VIEW", codes);
            Assert.DoesNotContain("ACCESS.ROLES.CREATE", codes);
        }
    }

    [Fact]
    public async Task NarrowingACeilingTakesThePermissionAwayAtOnce()
    {
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            Assert.Contains("BOOKINGS.CALENDAR.APPROVE", await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId));

            await connection.ExecuteAsync(
                """
                DELETE c FROM auth.RoleLevelPermissions c
                JOIN auth.RoleLevels l ON l.Id = c.RoleLevelId
                JOIN auth.Permissions p ON p.Id = c.PermissionId
                WHERE l.Code = 'STAFF' AND p.Code = 'BOOKINGS.CALENDAR.APPROVE';
                """,
                transaction: transaction);

            var codes = await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId);

            // The role still grants it; the level no longer allows it.
            Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", codes);
            Assert.Contains("BOOKINGS.CALENDAR.VIEW", codes);
        }
    }

    // ------------------------------------------------------------------ the ceiling's constraints

    [Fact]
    public async Task AnInstitutionLevelCannotHoldAPlatformPermission()
    {
        // CLAUDE.md: institution roles never contain Platform-scope permissions. The CHECK refuses the
        // honest insert; the composite FK refuses the one that lies about the permission's scope.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var platformPermissionId = await connection.ExecuteScalarAsync<int>(
                """
                INSERT auth.Permissions (Code, ScreenId, ActionId, Scope, Name)
                SELECT 'TEST.PLATFORM.EXECUTE', s.Id, a.Id, 'Platform', N'Test platform permission'
                FROM auth.Screens s CROSS JOIN auth.PermissionActions a
                WHERE s.Code = 'ACCESS.ROLES' AND a.Code = 'EXECUTE';
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """,
                transaction: transaction);

            var honest = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                """
                INSERT auth.RoleLevelPermissions (RoleLevelId, LevelScope, PermissionId, PermissionScope)
                SELECT l.Id, l.Scope, @platformPermissionId, 'Platform' FROM auth.RoleLevels l WHERE l.Code = 'OWNER';
                """,
                new { platformPermissionId },
                transaction));

            Assert.Contains("CK_RoleLevelPermissions_Scope", honest.Message, StringComparison.Ordinal);

            var lying = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                """
                INSERT auth.RoleLevelPermissions (RoleLevelId, LevelScope, PermissionId, PermissionScope)
                SELECT l.Id, l.Scope, @platformPermissionId, 'Institution' FROM auth.RoleLevels l WHERE l.Code = 'OWNER';
                """,
                new { platformPermissionId },
                transaction));

            Assert.Contains("FK_RoleLevelPermissions_Permission", lying.Message, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ the seed

    [Fact]
    public async Task TheCatalogueSeedChangesNothingTheSecondTime()
    {
        // It runs on every publish, so a second run must be a no-op - not even a history row, which
        // an UPDATE that sets a column to its own value would still write.
        const string Snapshot =
            """
            SELECT (SELECT COUNT(*) FROM auth.Modules)                     AS Modules,
                   (SELECT COUNT(*) FROM auth.Screens)                     AS Screens,
                   (SELECT COUNT(*) FROM auth.Permissions)                 AS Permissions,
                   (SELECT COUNT(*) FROM auth.PermissionDependencies)      AS Dependencies,
                   (SELECT COUNT(*) FROM auth.RoleLevelPermissions)        AS Ceilings,
                   (SELECT COUNT(*) FROM auth.RoleLevelPermissionsHistory) AS CeilingHistory,
                   (SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.Id = rp.RoleId
                    WHERE r.IsTemplate = 1)                                AS TemplateGrants,
                   (SELECT COUNT(*) FROM auth.RolePermissions)             AS AllGrants,
                   (SELECT COUNT(*) FROM auth.RolesHistory)                AS RoleHistory;
            """;

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var before = await connection.QuerySingleAsync(Snapshot, transaction: transaction);

            await connection.ExecuteAsync(CatalogueSeed(), transaction: transaction);

            var after = await connection.QuerySingleAsync(Snapshot, transaction: transaction);

            Assert.Equal((IDictionary<string, object>)before, (IDictionary<string, object>)after);
            Assert.Equal(9, (int)after.Permissions);
        }
    }

    [Fact]
    public async Task NarrowingACeilingInTheSeedRemovesItsRow()
    {
        // The ceilings are synced exactly, deletions included, so taking a permission out of the
        // seed takes it out of the database on the next publish.
        const string StaffApprove = "    ('BOOKINGS.CALENDAR.APPROVE'),\n    ('ACCESS.ROLES.VIEW'),";
        var seed = CatalogueSeed().ReplaceLineEndings("\n");
        Assert.Contains(StaffApprove, seed, StringComparison.Ordinal);

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            await connection.ExecuteAsync(
                seed.Replace(StaffApprove, "    ('ACCESS.ROLES.VIEW'),", StringComparison.Ordinal), transaction: transaction);

            Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId));
            Assert.Equal(4, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM auth.RoleLevelPermissions c JOIN auth.RoleLevels l ON l.Id = c.RoleLevelId
                WHERE l.Code = 'STAFF';
                """,
                transaction: transaction));
        }
    }

    [Theory]
    [InlineData("SELECT 'STAFF', v.Code", "SELECT 'STAF', v.Code", 51004)]
    [InlineData("('BOOKINGS.CALENDAR.VIEW')) AS v(Code);", "('BOOKINGS.CALENDAR.VIEW'), ('ACCESS.ROLES.CREATE')) AS v(Code);", 51005)]
    [InlineData("('ACCESS.ROLES.EDIT',         'ACCESS.ROLES.VIEW'),", "('ACCESS.ROLES.EDITT',        'ACCESS.ROLES.VIEW'),", 51003)]
    public async Task AMistakeInTheSeedStopsTheDeploy(string original, string mistake, int expectedError)
    {
        // A JOIN on a misspelt code would drop the row without a word; the seed checks instead. The
        // second case is a template granting outside its ceiling, which the view would silently drop.
        var seed = CatalogueSeed();
        Assert.Contains(original, seed, StringComparison.Ordinal);

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                connection.ExecuteAsync(seed.Replace(original, mistake, StringComparison.Ordinal), transaction: transaction));

            Assert.Equal(expectedError, error.Number);
        }
    }

    [Fact]
    public async Task EveryTemplateGrantIsInsideItsLevelsCeiling()
    {
        await using var connection = new SqlConnection(database.ConnectionString);

        var outside = await connection.QueryAsync<string>(
            """
            SELECT r.Code + ' ' + p.Code
            FROM auth.Roles r
            JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
            JOIN auth.Permissions p      ON p.Id = rp.PermissionId
            WHERE r.IsTemplate = 1
              AND NOT EXISTS (SELECT 1 FROM auth.RoleLevelPermissions c
                              WHERE c.RoleLevelId = r.RoleLevelId AND c.PermissionId = rp.PermissionId);
            """);

        Assert.Empty(outside);
    }
}

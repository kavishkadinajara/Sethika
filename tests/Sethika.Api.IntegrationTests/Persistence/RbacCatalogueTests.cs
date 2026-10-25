using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
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
        // The steward is at STAFF. INSTITUTION.PROFILE.VIEW is inside STAFF's ceiling; ACCESS.ROLES.VIEW
        // is not, since staff never see who else holds which role. Both are granted by hand, as a
        // buggy screen or a script might, with their modules on.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            await connection.ExecuteAsync(
                """
                INSERT auth.RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id FROM auth.Roles r CROSS JOIN auth.Permissions p
                WHERE r.Code = 'ALPHA_STEWARD' AND (p.Code = 'INSTITUTION.PROFILE.VIEW' OR p.Code = 'ACCESS.ROLES.VIEW');

                INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
                SELECT @institutionId, m.Id, 1, 'Manual', @userId FROM auth.Modules m WHERE m.Code IN ('ACCESS', 'INSTITUTION');
                """,
                new { institutionId = database.AlphaInstitutionId, userId = SethikaTestDatabase.AlphaUserId },
                transaction);

            var codes = await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId);

            Assert.Contains("INSTITUTION.PROFILE.VIEW", codes);
            Assert.DoesNotContain("ACCESS.ROLES.VIEW", codes);
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
            Assert.Equal(39, (int)after.Permissions);
        }
    }

    [Fact]
    public async Task NarrowingACeilingInTheSeedRemovesItsRow()
    {
        // The ceilings are synced exactly, deletions included, so taking a permission out of the
        // seed takes it out of the database on the next publish.
        const string StaffApprove = "    ('BOOKINGS.CALENDAR.VIEW'),\n    ('BOOKINGS.CALENDAR.APPROVE'),\n";
        var seed = CatalogueSeed().ReplaceLineEndings("\n");
        Assert.Contains(StaffApprove, seed, StringComparison.Ordinal);

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            await connection.ExecuteAsync(
                seed.Replace(StaffApprove, "    ('BOOKINGS.CALENDAR.VIEW'),\n", StringComparison.Ordinal), transaction: transaction);

            Assert.DoesNotContain("BOOKINGS.CALENDAR.APPROVE", await AlphaCodesAsync(connection, transaction, database.AlphaInstitutionId));
            Assert.Equal(10, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM auth.RoleLevelPermissions c JOIN auth.RoleLevels l ON l.Id = c.RoleLevelId
                WHERE l.Code = 'STAFF';
                """,
                transaction: transaction));
        }
    }

    [Theory]
    [InlineData("SELECT 'STAFF', v.Code", "SELECT 'STAF', v.Code", 51004)]
    [InlineData("('SERVICES.MYSCHEDULE.EDIT')) AS v(Code);", "('SERVICES.MYSCHEDULE.EDIT'), ('SERVICES.APPOINTMENTS.APPROVE')) AS v(Code);", 51005)]
    [InlineData("('ACCESS.ROLES.EDIT',         'ACCESS.ROLES.VIEW'),", "('ACCESS.ROLES.EDITT',        'ACCESS.ROLES.VIEW'),", 51003)]
    [InlineData("SELECT 'PLATFORM_REVIEWER', v.Code", "SELECT 'PLATFORM_REVIEWR', v.Code", 51006)]
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

    /// <summary>
    /// The reviewer's grants and PLATFORM_STAFF's ceiling are written alike, so a change meant for the
    /// grants is made in the grants section only.
    /// </summary>
    private static string WithReviewerGrants(string replacement)
    {
        const string Section = "system role grants */";
        const string ReviewerGrants = "    ('ONBOARDING.REQUESTS.VIEW'),\n    ('ONBOARDING.REQUESTS.EDIT')\n) AS v(Code);";
        var seed = CatalogueSeed().ReplaceLineEndings("\n");
        var at = seed.IndexOf(Section, StringComparison.Ordinal);
        Assert.True(at > 0, "the system role grants section is not in the seed");
        Assert.Contains(ReviewerGrants, seed[at..], StringComparison.Ordinal);

        return seed[..at] + seed[at..].Replace(ReviewerGrants, replacement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASystemRoleGrantOutsideItsCeilingStopsTheDeploy()
    {
        // The reviewer given the decision its level never allows.
        var mistaken = WithReviewerGrants(
            "    ('ONBOARDING.REQUESTS.VIEW'),\n    ('ONBOARDING.REQUESTS.EDIT'),\n    ('ONBOARDING.REQUESTS.APPROVE')\n) AS v(Code);");

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                connection.ExecuteAsync(mistaken, transaction: transaction));

            Assert.Equal(51006, error.Number);
        }
    }

    [Fact]
    public async Task TakingAGrantFromASystemRoleInTheSeedRemovesItsRow()
    {
        // Synced exactly, like the ceilings: a grant taken out of the seed is gone after the next publish.
        var narrowed = WithReviewerGrants("    ('ONBOARDING.REQUESTS.VIEW')\n) AS v(Code);");

        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            await connection.ExecuteAsync(narrowed, transaction: transaction);

            var reviewer = await connection.QueryAsync<string>(
                """
                SELECT p.Code FROM auth.RolePermissions rp
                JOIN auth.Roles r       ON r.Id = rp.RoleId
                JOIN auth.Permissions p ON p.Id = rp.PermissionId
                WHERE r.Code = 'PLATFORM_REVIEWER' AND r.InstitutionId IS NULL;
                """,
                transaction: transaction);

            Assert.Equal(["ONBOARDING.REQUESTS.VIEW"], reviewer);
        }
    }

    [Fact]
    public async Task EveryTemplateAndSystemRoleGrantIsInsideItsLevelsCeiling()
    {
        // The view drops a grant outside its ceiling without a word, so a seed that wrote one would
        // look applied and grant nothing. This is the check that it never does.
        await using var connection = new SqlConnection(database.ConnectionString);

        var outside = await connection.QueryAsync<string>(
            """
            SELECT r.Code + ' ' + p.Code
            FROM auth.Roles r
            JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
            JOIN auth.Permissions p      ON p.Id = rp.PermissionId
            WHERE (r.IsTemplate = 1 OR (r.IsSystem = 1 AND r.InstitutionId IS NULL))
              AND NOT EXISTS (SELECT 1 FROM auth.RoleLevelPermissions c
                              WHERE c.RoleLevelId = r.RoleLevelId AND c.PermissionId = rp.PermissionId);
            """);

        Assert.Empty(outside);
    }

    // ------------------------------------------------------------------ system role grants (plan 02, B1)

    [Theory]
    [InlineData("DONOR", "MYBOOKINGS.BOOKINGS.CREATE,MYBOOKINGS.BOOKINGS.EDIT,MYBOOKINGS.BOOKINGS.VIEW,MYSERVICES.APPOINTMENTS.CREATE,MYSERVICES.APPOINTMENTS.EDIT,MYSERVICES.APPOINTMENTS.VIEW,ONBOARDING.APPLICATION.CREATE,ONBOARDING.APPLICATION.EDIT,ONBOARDING.APPLICATION.VIEW")]
    [InlineData("PLATFORM_ADMIN", "ONBOARDING.REQUESTS.APPROVE,ONBOARDING.REQUESTS.EDIT,ONBOARDING.REQUESTS.REJECT,ONBOARDING.REQUESTS.VIEW")]
    [InlineData("PLATFORM_REVIEWER", "ONBOARDING.REQUESTS.EDIT,ONBOARDING.REQUESTS.VIEW")]
    public async Task EachSystemRoleHoldsExactlyItsGrants(string roleCode, string expected)
    {
        // Read back through the view, as /api/me/permissions reads it: a person holding only this role,
        // with no institution selected. The reviewer reviews but never decides.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var userId = Guid.CreateVersion7();
            await connection.ExecuteAsync(
                """
                INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PreferredLanguage, Status)
                VALUES (@userId, 'Donor', N'Catalogue Test', @phone, 'en', 'Active');
                INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
                SELECT @userId, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = @roleCode AND r.InstitutionId IS NULL;
                """,
                new { userId, roleCode, phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture) },
                transaction);

            var codes = await connection.QueryAsync<string>(
                """
                SELECT PermissionCode FROM auth.vw_EffectivePermissions
                WHERE UserId = @userId AND InstitutionId IS NULL
                ORDER BY PermissionCode;
                """,
                new { userId },
                transaction);

            Assert.Equal(expected.Split(','), codes);
        }
    }

    [Fact]
    public async Task OnlyAnOnboardingDecisionNeedsAStepUp()
    {
        // Approving creates a tenant with an owner and rejecting ends an application for good; nothing
        // else in onboarding is worth an SMS code.
        await using var connection = new SqlConnection(database.ConnectionString);

        var stepUp = await connection.QueryAsync<string>(
            "SELECT Code FROM auth.Permissions WHERE Code LIKE 'ONBOARDING.%' AND RequiresMfa = 1 ORDER BY Code;");

        Assert.Equal(["ONBOARDING.REQUESTS.APPROVE", "ONBOARDING.REQUESTS.REJECT"], stepUp);
    }

    [Fact]
    public async Task NoBookingDecisionNeedsAStepUp()
    {
        // Plan 03, E1: approving a donor's request is the day's work, and a step-up would cost an SMS
        // every fifteen minutes. The step-up tests mark a permission themselves and put it back
        // (SethikaTestDatabase.RequireStepUpAsync); a run killed between the two fails here first.
        await using var connection = new SqlConnection(database.ConnectionString);

        var stepUp = await connection.QueryAsync<string>(
            "SELECT Code FROM auth.Permissions WHERE Code LIKE 'BOOKINGS.%' AND RequiresMfa = 1 ORDER BY Code;");

        Assert.Empty(stepUp);
    }

    // ------------------------------------------------------------------ bookings (plan 03, 5.1)

    [Fact]
    public async Task StaffMayDecideBookingsButNeverSetUpTheSlots()
    {
        // Deciding, completing and cancelling a booking is the day's work; what is offered, and when,
        // is the institution's to set. A new staff member starts able to read, not to decide.
        await using var connection = new SqlConnection(database.ConnectionString);

        var ceiling = await connection.QueryAsync<string>(
            """
            SELECT p.Code FROM auth.RoleLevelPermissions c
            JOIN auth.RoleLevels l  ON l.Id = c.RoleLevelId
            JOIN auth.Permissions p ON p.Id = c.PermissionId
            WHERE l.Code = 'STAFF' AND p.Code LIKE 'BOOKINGS.%'
            ORDER BY p.Code;
            """);

        var template = await connection.QueryAsync<string>(
            """
            SELECT p.Code FROM auth.Roles r
            JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
            JOIN auth.Permissions p      ON p.Id = rp.PermissionId
            WHERE r.Code = 'STAFF' AND r.IsTemplate = 1 AND r.InstitutionId IS NULL
            ORDER BY p.Code;
            """);

        Assert.Equal(
            ["BOOKINGS.CALENDAR.APPROVE", "BOOKINGS.CALENDAR.EDIT", "BOOKINGS.CALENDAR.REJECT", "BOOKINGS.CALENDAR.VIEW", "BOOKINGS.SLOTS.VIEW"],
            ceiling);
        Assert.Equal(
            ["BOOKINGS.CALENDAR.VIEW", "BOOKINGS.SLOTS.VIEW", "INSTITUTION.PROFILE.VIEW",
             "SERVICES.APPOINTMENTS.VIEW", "SERVICES.LEADERS.VIEW", "SERVICES.TYPES.VIEW"],
            template);
    }

    [Fact]
    public async Task ADonorsBookingsAreTheirOwnAndNeverAnInstitutionsGrant()
    {
        // Self scope, and only the donor level's ceiling. CK_RoleLevelPermissions_Scope lets a Self
        // permission sit at any level, so it is the seed alone that keeps "book a meal" out of an
        // institution role: staff holding it there would still book as themselves, not the donor.
        await using var connection = new SqlConnection(database.ConnectionString);

        var scopes = await connection.QueryAsync<string>(
            "SELECT Code + ' ' + Scope FROM auth.Permissions WHERE Code LIKE 'MYBOOKINGS.%' ORDER BY Code;");

        var institutionLevels = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.RoleLevelPermissions c
            JOIN auth.Permissions p ON p.Id = c.PermissionId
            WHERE p.Code LIKE 'MYBOOKINGS.%' AND c.LevelScope <> 'Donor';
            """);

        Assert.Equal(["MYBOOKINGS.BOOKINGS.CREATE Self", "MYBOOKINGS.BOOKINGS.EDIT Self", "MYBOOKINGS.BOOKINGS.VIEW Self"], scopes);
        Assert.Equal(0, institutionLevels);
    }

    // ------------------------------------------------------------------ religious services (Phase E)

    [Fact]
    public async Task ALeaderHoldsOnlyTheirOwnScheduleAndNobodyElseIsGivenIt()
    {
        // A leader's schedule is theirs: the LEADER template grants it and nothing else, and the
        // owner and manager templates leave it out (an owner who also leads is given LEADER too).
        await using var connection = new SqlConnection(database.ConnectionString);

        var grants = (await connection.QueryAsync<(string Role, string Permission)>(
            """
            SELECT r.Code AS Role, p.Code AS Permission FROM auth.Roles r
            JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
            JOIN auth.Permissions p      ON p.Id = rp.PermissionId
            WHERE r.IsTemplate = 1 AND r.InstitutionId IS NULL
              AND (r.Code = 'LEADER' OR p.Code LIKE 'SERVICES.MYSCHEDULE.%')
            ORDER BY r.Code, p.Code;
            """)).ToList();

        Assert.Equal([("LEADER", "SERVICES.MYSCHEDULE.EDIT"), ("LEADER", "SERVICES.MYSCHEDULE.VIEW")], grants);
        Assert.Equal("STAFF", await connection.ExecuteScalarAsync<string>(
            "SELECT l.Code FROM auth.Roles r JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId WHERE r.Code = 'LEADER' AND r.IsTemplate = 1;"));
    }

    [Fact]
    public async Task ReligiousServicesAreOnByDefaultForReligiousInstitutionsOnly()
    {
        // V12: not core, so a care home does not get it; the religious types do, by Category.
        await using var connection = new SqlConnection(database.ConnectionString);

        var byType = await connection.QueryAsync<string>(
            """
            SELECT t.Code + ' ' + t.Category + ' ' + CASE WHEN d.ModuleId IS NULL THEN 'off' ELSE 'on' END
            FROM org.InstitutionTypes t
            LEFT JOIN org.InstitutionTypeDefaultModules d
                   ON d.InstitutionTypeId = t.Id AND d.ModuleId = (SELECT Id FROM auth.Modules WHERE Code = 'SERVICES')
            ORDER BY t.Code;
            """);

        Assert.All(byType, row => Assert.EndsWith(row.Contains(" Religious ", StringComparison.Ordinal) ? " on" : " off", row, StringComparison.Ordinal));
        Assert.Contains(byType, row => row.EndsWith(" off", StringComparison.Ordinal));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM auth.Modules WHERE Code = 'SERVICES' AND IsCore = 1;"));
    }
}

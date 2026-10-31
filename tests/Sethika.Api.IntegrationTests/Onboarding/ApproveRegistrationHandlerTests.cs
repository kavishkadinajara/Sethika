using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Onboarding.Approval;
using Sethika.Domain.Onboarding;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Onboarding;

/// <summary>
/// Plan 02, B6: approving an application creates the whole institution in one transaction, or
/// nothing at all. Against the real database, as the platform, under Row-Level Security.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ApproveRegistrationHandlerTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string RequestNo = "REQ-TEST-000101";
    private readonly OnboardingScenario _scenario = new(database);

    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => database.SeedAsync();

    private static ApproveRegistrationHandler HandlerFor(SethikaDbContext context) =>
        new(context,
            new SqlRegistrationLocks(context),
            new SqlNumberSeries(context),
            new SqlInstitutionModules(context),
            new FixedTimeProvider(new DateTimeOffset(OnboardingScenario.Now.AddHours(2), TimeSpan.Zero)));

    private async Task<ApproveRegistrationResult> ApproveAsync(string requestNo = RequestNo, Guid? decider = null)
    {
        await using var context = _scenario.AsPlatform(decider ?? OnboardingScenario.Reviewer);
        return await HandlerFor(context).HandleAsync(new ApproveRegistrationCommand(requestNo, decider ?? OnboardingScenario.Reviewer, "198.51.100.7"));
    }

    private async Task<DbConnection> PlatformAsync() =>
        await new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform()).OpenConnectionAsync();

    /// <summary>Everything an approval could have written, counted, so "nothing was created" can be checked.</summary>
    private async Task<Dictionary<string, int>> FootprintAsync()
    {
        await using var connection = await PlatformAsync();
        var row = (IDictionary<string, object>)await connection.QuerySingleAsync(
            """
            SELECT (SELECT COUNT(*) FROM org.Institutions)                       AS Institutions,
                   (SELECT COUNT(*) FROM org.InstitutionDocuments)               AS Documents,
                   (SELECT COUNT(*) FROM org.InstitutionModules)                 AS Modules,
                   (SELECT COUNT(*) FROM auth.Roles WHERE InstitutionId IS NOT NULL) AS Roles,
                   (SELECT COUNT(*) FROM auth.RolePermissions)                   AS Grants,
                   (SELECT COUNT(*) FROM org.InstitutionMembers)                 AS Members,
                   (SELECT COUNT(*) FROM auth.UserRoles)                         AS UserRoles,
                   (SELECT COUNT(*) FROM org.InstitutionStatusHistory)           AS StatusHistory,
                   (SELECT COUNT(*) FROM audit.AuditLogs)                        AS Audit,
                   (SELECT COUNT(*) FROM core.OutboxMessages)                    AS Outbox,
                   (SELECT PermissionsVersion FROM auth.Users WHERE Id = @applicant) AS ApplicantVersion,
                   (SELECT ISNULL(MAX(LastNumber), 0) FROM core.NumberSeries
                    WHERE SeriesCode = 'INSTITUTION' AND ScopeKey = 'PLATFORM' AND SeriesYear = 2026) AS LastInstitutionNo,
                   (SELECT COUNT(*) FROM org.RegistrationRequests WHERE Status = 'UnderReview') AS UnderReview;
            """,
            new { applicant = OnboardingScenario.Applicant });

        return row.ToDictionary(kv => kv.Key, kv => Convert.ToInt32(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------ the whole institution

    [Fact]
    public async Task AnApprovalCreatesTheWholeInstitutionAndMakesTheApplicantItsOwner()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");
        var before = await FootprintAsync();

        var result = await ApproveAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Matches("^INS-2026-[0-9]{6}$", result.InstitutionCode);
        Assert.Equal("sri-sudharmaramaya-galle", result.Slug);

        await using var connection = await PlatformAsync();
        var institution = await connection.QuerySingleAsync<(int Id, string Status, bool IsPubliclyListed, Guid ApprovedBy, string Name, int RequestId)>(
            """
            SELECT i.Id, i.Status, i.IsPubliclyListed, i.ApprovedBy, i.Name, i.RegistrationRequestId
            FROM org.Institutions i WHERE i.InstitutionCode = @code;
            """,
            new { code = result.InstitutionCode });
        Assert.Equal(("Active", false, OnboardingScenario.Reviewer, "Sri Sudharmaramaya"),
            (institution.Status, institution.IsPubliclyListed, institution.ApprovedBy, institution.Name));

        var request = await connection.QuerySingleAsync<(int Id, string Status, int? CreatedInstitutionId, Guid? DecidedBy)>(
            "SELECT Id, Status, CreatedInstitutionId, DecidedBy FROM org.RegistrationRequests WHERE RequestNo = @RequestNo;", new { RequestNo });
        Assert.Equal((request.Id, RegistrationStatus.Approved, (int?)institution.Id, (Guid?)OnboardingScenario.Reviewer),
            (institution.RequestId, request.Status, request.CreatedInstitutionId, request.DecidedBy));

        // Documents: every verified upload, the same file, traced to the upload it came from.
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM org.InstitutionDocuments d
            JOIN org.RegistrationRequestDocuments s ON s.Id = d.SourceRequestDocumentId AND s.FileId = d.FileId
            WHERE d.InstitutionId = @id AND d.Status = 'Valid';
            """, new { id = institution.Id }));

        // Modules: the institution portal's three core modules, and religious services, which a
        // temple has by default (Phase E, V12).
        Assert.Equal(["ACCESS", "BOOKINGS", "INSTITUTION", "SERVICES"], await connection.QueryAsync<string>(
            """
            SELECT m.Code FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId
            WHERE im.InstitutionId = @id AND im.IsEnabled = 1 AND im.Source = 'Default' ORDER BY m.Code;
            """, new { id = institution.Id }));

        // Roles: one per template, granting exactly what the template grants.
        Assert.Empty(await connection.QueryAsync<string>(
            """
            WITH Clone AS (
                SELECT c.Code, rp.PermissionId FROM auth.Roles c JOIN auth.RolePermissions rp ON rp.RoleId = c.Id
                WHERE c.InstitutionId = @id),
            Template AS (
                SELECT t.Code, rp.PermissionId FROM auth.Roles t JOIN auth.RolePermissions rp ON rp.RoleId = t.Id
                WHERE t.IsTemplate = 1 AND t.InstitutionId IS NULL)
            SELECT COALESCE(c.Code, t.Code) FROM Clone c FULL JOIN Template t ON t.Code = c.Code AND t.PermissionId = c.PermissionId
            WHERE c.Code IS NULL OR t.Code IS NULL;
            """, new { id = institution.Id }));
        Assert.Equal(["LEADER", "MANAGER", "OWNER", "STAFF"], await connection.QueryAsync<string>(
            "SELECT Code FROM auth.Roles WHERE InstitutionId = @id AND TemplateRoleId IS NOT NULL ORDER BY Code;", new { id = institution.Id }));

        // The owner, who now holds the owner's permissions there - read through the view, as the API does.
        Assert.Equal(("Secretary", true), await connection.QuerySingleAsync<(string, bool)>(
            "SELECT Designation, IsOwner FROM org.InstitutionMembers WHERE InstitutionId = @id AND UserId = @applicant;",
            new { id = institution.Id, applicant = OnboardingScenario.Applicant }));
        Assert.Equal(
            await connection.QueryAsync<string>(
                """
                SELECT p.Code FROM auth.Roles t JOIN auth.RolePermissions rp ON rp.RoleId = t.Id JOIN auth.Permissions p ON p.Id = rp.PermissionId
                WHERE t.Code = 'OWNER' AND t.IsTemplate = 1 ORDER BY p.Code;
                """),
            await connection.QueryAsync<string>(
                "SELECT PermissionCode FROM auth.vw_EffectivePermissions WHERE UserId = @applicant AND InstitutionId = @id ORDER BY PermissionCode;",
                new { id = institution.Id, applicant = OnboardingScenario.Applicant }));

        // The trail: both histories, one audit row, one outbox message - and no personal data in it.
        var after = await FootprintAsync();
        Assert.Equal(before["ApplicantVersion"] + 1, after["ApplicantVersion"]);
        Assert.Equal(before["StatusHistory"] + 1, after["StatusHistory"]);
        Assert.Equal(before["Audit"] + 1, after["Audit"]);
        Assert.Equal(before["Outbox"] + 1, after["Outbox"]);

        var payload = await connection.ExecuteScalarAsync<string>(
            "SELECT Payload FROM core.OutboxMessages WHERE EventType = 'InstitutionApproved' AND AggregateId = @RequestNo;", new { RequestNo });
        Assert.Contains(result.InstitutionCode!, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(OnboardingScenario.ApplicantNic, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(OnboardingScenario.ApplicantPhone, payload, StringComparison.Ordinal);

        Assert.Equal(("RegistrationRequest", "198.51.100.7"), await connection.QuerySingleAsync<(string, string)>(
            "SELECT EntityName, IpAddress FROM audit.AuditLogs WHERE Action = 'Approve' AND EntityId = @RequestNo;", new { RequestNo }));
    }

    [Fact]
    public async Task ACoreModuleOfAnotherPortalIsNeverAnInstitutions()
    {
        // Core means "on in every institution" only within the institution portal. A core module of
        // the donor portal, planted for this test, must not reach the new institution.
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");
        await using var connection = await PlatformAsync();
        await connection.ExecuteAsync(
            """
            INSERT auth.Modules (PortalId, Code, Name, SortOrder, IsCore, IsActive)
            SELECT p.Id, 'TEST_DONOR_CORE', N'Test donor core module', 99, 1, 1 FROM auth.Portals p WHERE p.Code = 'DONOR';
            """);

        try
        {
            Assert.True((await ApproveAsync()).Succeeded);

            Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId WHERE m.Code = 'TEST_DONOR_CORE';"));
        }
        finally
        {
            await connection.ExecuteAsync(
                """
                DELETE im FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId WHERE m.Code = 'TEST_DONOR_CORE';
                DELETE FROM auth.Modules WHERE Code = 'TEST_DONOR_CORE';
                """);
        }
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task AnApplicationWithAFailedChecklistItemIsRefusedAndNothingIsCreated()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");
        await using (var reviewer = _scenario.AsReviewer())
        {
            var request = await reviewer.RegistrationRequests.Include(r => r.ChecklistEntries).SingleAsync(r => r.RequestNo == RequestNo);
            var item = request.ChecklistEntries.First().ChecklistItemId;
            Assert.True(request.RecordChecklist(item, passed: false, "No answer", OnboardingScenario.Reviewer, OnboardingScenario.Now).Succeeded);
            await reviewer.SaveChangesAsync();
        }

        var before = await FootprintAsync();

        Assert.Equal(RegistrationErrors.ChecklistIncomplete, (await ApproveAsync()).Error);
        Assert.Equal(before, await FootprintAsync());
    }

    [Fact]
    public async Task NobodyApprovesTheirOwnApplication_EvenAsThePlatform()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");

        Assert.Equal(RegistrationErrors.OwnApplication, (await ApproveAsync(decider: OnboardingScenario.Applicant)).Error);

        // And the database refuses it on its own, should a handler ever forget.
        await using var connection = await PlatformAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE org.RegistrationRequests SET DecidedBy = ApplicantUserId, DecidedAt = SYSUTCDATETIME() WHERE RequestNo = @RequestNo;",
            new { RequestNo }));
        Assert.Contains("CK_RegReq_DeciderNotApplicant", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownApplicationIsNotFound()
    {
        Assert.Equal(ApproveRegistrationResult.NotFound, (await ApproveAsync("REQ-TEST-999999")).Error);
    }

    // ------------------------------------------------------------------ all or nothing

    [Fact]
    public async Task AFailureAtTheLastStepLeavesNothingBehind_TheInstitutionNumberIncluded()
    {
        // The audit row is written in the last save. A CHECK that refuses it, planted for this test,
        // fails the approval after the institution, its modules, roles and owner were already written
        // inside the transaction - every one of which must roll back with it.
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");
        var before = await FootprintAsync();

        await using var connection = await PlatformAsync();
        await connection.ExecuteAsync("ALTER TABLE audit.AuditLogs WITH NOCHECK ADD CONSTRAINT CK_Test_NoApprovals CHECK (Action <> 'Approve');");
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => ApproveAsync());
        }
        finally
        {
            await connection.ExecuteAsync("ALTER TABLE audit.AuditLogs DROP CONSTRAINT CK_Test_NoApprovals;");
        }

        Assert.Equal(before, await FootprintAsync());
    }

    // ------------------------------------------------------------------ concurrency, forced

    [Fact]
    public async Task TwoApprovalsOfOneApplicationAtOnceCreateOneInstitution()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");

        var results = await RaceAsync(() => ApproveAsync(), () => ApproveAsync());

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == RegistrationErrors.InvalidTransition);

        await using var connection = await PlatformAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM org.Institutions i JOIN org.RegistrationRequests r ON r.Id = i.RegistrationRequestId WHERE r.RequestNo = @RequestNo;",
            new { RequestNo }));
    }

    [Fact]
    public async Task TwoInstitutionsOfTheSameNameApprovedAtOnceGetDifferentSlugs()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/77");
        await _scenario.ReadyToApproveAsync("REQ-TEST-000102", "BUD/GAL/1960/12");

        var results = await RaceAsync(() => ApproveAsync(), () => ApproveAsync("REQ-TEST-000102"));

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        Assert.Equal(["sri-sudharmaramaya-galle", "sri-sudharmaramaya-galle-2"], results.Select(r => r.Slug!).Order());
    }

    /// <summary>
    /// Holds the decision lock, starts both approvals, waits until both are queued on it, then lets
    /// them go together.
    /// </summary>
    private async Task<ApproveRegistrationResult[]> RaceAsync(params Func<Task<ApproveRegistrationResult>>[] approvals)
    {
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "EXEC sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction';",
            new { resource = SqlRegistrationLocks.DecisionLock },
            hold);

        var running = approvals.Select(a => Task.Run(a)).ToArray();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        await using (var watcher = new SqlConnection(database.ConnectionString))
        {
            while (await watcher.ExecuteScalarAsync<int>(
                       """
                       SELECT COUNT(*) FROM sys.dm_tran_locks
                       WHERE resource_database_id = DB_ID() AND resource_type = 'APPLICATION' AND request_status = 'WAIT';
                       """) < approvals.Length)
            {
                Assert.True(DateTime.UtcNow < deadline, "The approvals never queued on the decision lock.");
                await Task.Delay(50);
            }
        }

        await hold.RollbackAsync();
        return await Task.WhenAll(running);
    }
}

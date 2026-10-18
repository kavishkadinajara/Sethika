using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Onboarding;

/// <summary>
/// Plan 02, 5.4: the platform's review over HTTP, against SQL Server, Redis and a real file root.
/// Alpha applies (<see cref="OnboardingScenario"/>), a reviewer made here holds PLATFORM_REVIEWER,
/// and Beta is given PLATFORM_ADMIN for the decisions. Step-ups are written straight into Redis:
/// the step-up flow has its own tests.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RegistrationReviewEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "registration-review-test-signing-key-32-bytes!!";
    private const string Base = "/api/admin/registrations";

    private static readonly Guid Admin = SethikaTestDatabase.BetaUserId;
    private static readonly string[] OneQuestion = ["Please send a clearer copy of the certificate."];
    private static readonly string[] BlankQuestion = [" "];

    private readonly Guid _reviewer = Guid.CreateVersion7();
    private readonly string _files = Path.Combine(Path.GetTempPath(), "sethika-review-files-" + Guid.NewGuid().ToString("N"));
    private readonly OnboardingScenario _scenario = new(database);
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        Directory.CreateDirectory(_files);
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, Email, PhoneConfirmed, PreferredLanguage, Status)
            VALUES (@reviewer, 'PlatformStaff', N'Test Reviewer', @email, 0, 'en', 'Active');
            """,
            new { reviewer = _reviewer, email = $"reviewer-{_reviewer:N}@sethika.test" });
        await GrantRoleAsync(_reviewer, "PLATFORM_REVIEWER");
        await GrantRoleAsync(Admin, "PLATFORM_ADMIN");

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Files:LocalRoot", _files);
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();

        // The reset removes the test applications, and with them every row the reviewer wrote.
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE ur FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
            WHERE (ur.UserId = @admin AND r.Code = 'PLATFORM_ADMIN') OR ur.UserId = @reviewer OR (ur.UserId = @applicant AND r.Code = 'PLATFORM_ADMIN');
            UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id IN (@admin, @applicant);
            DELETE FROM core.OutboxMessages WHERE AggregateId LIKE 'REQ-TEST-R%';
            DELETE FROM audit.AuditLogs WHERE EntityId LIKE 'REQ-TEST-R%';
            DELETE FROM auth.Users WHERE Id = @reviewer;
            """,
            new { admin = Admin, reviewer = _reviewer, applicant = OnboardingScenario.Applicant });

        await _redis.DisposeAsync();
        Directory.Delete(_files, recursive: true);
    }

    // ------------------------------------------------------------------ helpers

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;");
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException($"No value for: {sql}");
    }

    /// <summary>Gives a global role, and moves the user's permissions version so no cached set hides it.</summary>
    private Task GrantRoleAsync(Guid userId, string roleCode) => ExecuteAsync(
        """
        INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
        SELECT @userId, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = @roleCode AND r.InstitutionId IS NULL;
        UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @userId;
        """,
        new { userId, roleCode });

    /// <summary>A client in a platform session for <paramref name="userId"/>, and its token's id for a step-up.</summary>
    private (HttpClient Client, string TokenId) PlatformClient(Guid userId)
    {
        var tokens = JwtTokenServiceTests.CreateService(SigningKey, TimeProvider.System)
            .Issue(new TokenSubject(userId, InstitutionId: null, IsPlatform: true));

        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return (client, tokens.Access.JwtId);
    }

    private HttpClient Reviewer() => PlatformClient(_reviewer).Client;

    /// <summary>The admin's client, stepped up (plan A5) unless told otherwise.</summary>
    private async Task<HttpClient> AdminAsync(bool steppedUp = true)
    {
        var (client, tokenId) = PlatformClient(Admin);
        if (steppedUp)
        {
            await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(Admin, tokenId), 1, TimeSpan.FromMinutes(15));
        }

        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.Ordinal) == true, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString();

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await CodeOf(response));
    }

    private Task<string> StatusOfAsync(string requestNo) =>
        ScalarAsync<string>("SELECT Status FROM org.RegistrationRequests WHERE RequestNo = @requestNo;", new { requestNo });

    private async Task<string[]> CodesAsync(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<string>(sql)];
    }

    private Task<string[]> MandatoryCodesAsync() => CodesAsync(
        """
        SELECT dt.Code FROM org.InstitutionTypeRequiredDocuments r
        JOIN org.InstitutionTypes t ON t.Id = r.InstitutionTypeId AND t.Code = 'TEMPLE'
        JOIN org.DocumentTypes dt ON dt.Id = r.DocumentTypeId
        WHERE r.IsMandatory = 1
        ORDER BY dt.Code;
        """);

    /// <summary>The active checklist items that apply to a temple.</summary>
    private Task<string[]> ChecklistCodesAsync() => CodesAsync(
        """
        SELECT i.Code FROM org.VerificationChecklistItems i
        WHERE i.IsActive = 1 AND (i.InstitutionTypeId IS NULL OR i.InstitutionTypeId = (SELECT Id FROM org.InstitutionTypes WHERE Code = 'TEMPLE'))
        ORDER BY i.SortOrder, i.Code;
        """);

    /// <summary>Submitted, then under review by the test's reviewer.</summary>
    private async Task UnderReviewAsync(string requestNo, string registrationNo)
    {
        await _scenario.SubmittedAsync(requestNo, registrationNo);
        using var started = await Reviewer().PostAsync($"{Base}/{requestNo}/start-review", null);
        Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
    }

    // ------------------------------------------------------------------ the queue

    [Fact]
    public async Task TheQueueIsOldestSubmittedFirst_AndADraftIsNeverShown()
    {
        await _scenario.SubmittedAsync("REQ-TEST-R01", "BUD/R/01");
        await _scenario.SubmittedAsync("REQ-TEST-R02", "BUD/R/02");
        await _scenario.SubmittedAsync("REQ-TEST-R03", "BUD/R/03", submit: false);
        await ExecuteAsync(
            "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1; UPDATE org.RegistrationRequests SET SubmittedAt = DATEADD(DAY, -1, SubmittedAt) WHERE RequestNo = 'REQ-TEST-R02';");
        var reviewer = Reviewer();

        var page = await JsonAsync(await reviewer.GetAsync(Base + "?pageSize=100"));
        var numbers = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestNo").GetString()).ToList();
        Assert.True(numbers.IndexOf("REQ-TEST-R02") < numbers.IndexOf("REQ-TEST-R01") && numbers.IndexOf("REQ-TEST-R02") >= 0, string.Join(", ", numbers));
        Assert.DoesNotContain("REQ-TEST-R03", numbers);

        await AssertRefusedAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R03"), HttpStatusCode.NotFound, "registration_not_found");
        await AssertRefusedAsync(await reviewer.PostAsync($"{Base}/REQ-TEST-R03/start-review", null), HttpStatusCode.NotFound, "registration_not_found");
        await AssertRefusedAsync(await reviewer.GetAsync(Base + "?status=Draft"), HttpStatusCode.BadRequest, "invalid_status");

        var approved = await JsonAsync(await reviewer.GetAsync(Base + "?status=Approved"));
        Assert.DoesNotContain(approved.GetProperty("items").EnumerateArray(), i => i.GetProperty("requestNo").GetString()!.StartsWith("REQ-TEST-R0", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the whole review

    [Fact]
    public async Task AReviewerStartsVerifiesAndChecks_AndAnAdminApprovesWithAStepUp()
    {
        await UnderReviewAsync("REQ-TEST-R10", "BUD/R/10");
        var reviewer = Reviewer();

        var detail = await JsonAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R10"));
        Assert.Equal("UnderReview", detail.GetProperty("application").GetProperty("status").GetString());
        Assert.NotEmpty(detail.GetProperty("rejectReasons").EnumerateArray());

        foreach (var code in await MandatoryCodesAsync())
        {
            using var verified = await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R10/documents/{code.ToLowerInvariant()}/verification", new { verified = true });
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        }

        var items = (await ChecklistCodesAsync()).Select(code => new { code, passed = true, remarks = "Seen" }).ToArray();
        using (var checklist = await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R10/checklist", new { items }))
        {
            Assert.Equal(HttpStatusCode.NoContent, checklist.StatusCode);
        }

        detail = await JsonAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R10"));
        Assert.All(detail.GetProperty("checklist").EnumerateArray(), i => Assert.True(i.GetProperty("isPassed").GetBoolean()));
        Assert.All(detail.GetProperty("application").GetProperty("documents").EnumerateArray(), d => Assert.Equal("Verified", d.GetProperty("verificationStatus").GetString()));

        // The decision needs a step-up.
        await AssertRefusedAsync(await (await AdminAsync(steppedUp: false)).PostAsync($"{Base}/REQ-TEST-R10/approve", null), HttpStatusCode.Forbidden, "stepup_required");
        Assert.Equal("UnderReview", await StatusOfAsync("REQ-TEST-R10"));

        using var approve = await (await AdminAsync()).PostAsync($"{Base}/REQ-TEST-R10/approve", null);
        var approved = await JsonAsync(approve);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.StartsWith("INS-", approved.GetProperty("institutionCode").GetString(), StringComparison.Ordinal);

        Assert.Equal("Approved", await StatusOfAsync("REQ-TEST-R10"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateId = 'REQ-TEST-R10' AND EventType = 'InstitutionApproved';"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLogs WHERE EntityId = 'REQ-TEST-R10' AND Action = 'Approve' AND UserId = @admin;", new { admin = Admin }));
    }

    [Fact]
    public async Task AReviewerCannotApproveOrReject_EvenSteppedUp()
    {
        await UnderReviewAsync("REQ-TEST-R11", "BUD/R/11");
        var (reviewer, tokenId) = PlatformClient(_reviewer);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(_reviewer, tokenId), 1, TimeSpan.FromMinutes(15));

        using var approve = await reviewer.PostAsync($"{Base}/REQ-TEST-R11/approve", null);
        using var reject = await reviewer.PostAsJsonAsync($"{Base}/REQ-TEST-R11/reject", new { reason = "NOT_ELIGIBLE" });

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (approve.StatusCode, reject.StatusCode));
        Assert.Equal("UnderReview", await StatusOfAsync("REQ-TEST-R11"));
    }

    // ------------------------------------------------------------------ the steps

    [Fact]
    public async Task ADocumentIsRefusedOnlyWithRemarks_AndOnlyUnderReview()
    {
        await _scenario.SubmittedAsync("REQ-TEST-R20", "BUD/R/20");
        var reviewer = Reviewer();
        var code = (await MandatoryCodesAsync())[0];

        // Before the review starts.
        await AssertRefusedAsync(
            await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R20/documents/{code}/verification", new { verified = true }),
            HttpStatusCode.Conflict,
            "invalid_transition");

        using (var started = await reviewer.PostAsync($"{Base}/REQ-TEST-R20/start-review", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
        }

        await AssertRefusedAsync(
            await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R20/documents/{code}/verification", new { verified = false, remarks = "  " }),
            HttpStatusCode.BadRequest,
            "remarks_required");
        await AssertRefusedAsync(
            await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R20/documents/NO_SUCH_TYPE/verification", new { verified = true }),
            HttpStatusCode.NotFound,
            "document_not_found");

        using var refused = await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R20/documents/{code}/verification", new { verified = false, remarks = "The seal is not readable." });
        Assert.Equal(HttpStatusCode.NoContent, refused.StatusCode);

        var document = (await JsonAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R20"))).GetProperty("application").GetProperty("documents")
            .EnumerateArray().Single(d => d.GetProperty("documentType").GetString() == code);
        Assert.Equal(("Rejected", "The seal is not readable."), (document.GetProperty("verificationStatus").GetString(), document.GetProperty("reviewerRemarks").GetString()));
    }

    [Fact]
    public async Task ChecklistAnswersMustBeItemsThatApply_AllOrNone()
    {
        await UnderReviewAsync("REQ-TEST-R21", "BUD/R/21");
        var reviewer = Reviewer();
        var first = (await ChecklistCodesAsync())[0];

        await AssertRefusedAsync(
            await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R21/checklist", new { items = new object[] { new { code = first, passed = true }, new { code = "NO_SUCH_ITEM", passed = true } } }),
            HttpStatusCode.BadRequest,
            "checklist_item_unknown");

        using var duplicate = await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R21/checklist", new { items = new object[] { new { code = first, passed = true }, new { code = first.ToLowerInvariant(), passed = false } } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(["items[1].code"], (await JsonAsync(duplicate)).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));

        // A real, active item - for children's homes, not temples.
        await ExecuteAsync(
            """
            INSERT org.VerificationChecklistItems (InstitutionTypeId, Code, Question, IsMandatory, SortOrder, IsActive)
            SELECT t.Id, 'TEST_CHILDREN_ONLY', N'Test item', 1, 99, 1 FROM org.InstitutionTypes t WHERE t.Code = 'CHILDRENS_HOME';
            """);
        try
        {
            await AssertRefusedAsync(
                await reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R21/checklist", new { items = new object[] { new { code = "TEST_CHILDREN_ONLY", passed = true } } }),
                HttpStatusCode.BadRequest,
                "checklist_item_unknown");
        }
        finally
        {
            await ExecuteAsync("DELETE FROM org.VerificationChecklistItems WHERE Code = 'TEST_CHILDREN_ONLY';");
        }

        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM org.RegistrationRequestChecklist c JOIN org.RegistrationRequests r ON r.Id = c.RequestId WHERE r.RequestNo = 'REQ-TEST-R21';"));
    }

    [Fact]
    public async Task QuestionsSendTheApplicationBack_WithATextToTheApplicant()
    {
        await UnderReviewAsync("REQ-TEST-R22", "BUD/R/22");
        var reviewer = Reviewer();

        await AssertRefusedAsync(await reviewer.PostAsJsonAsync($"{Base}/REQ-TEST-R22/questions", new { questions = BlankQuestion }), HttpStatusCode.BadRequest, "question_required");
        await AssertRefusedAsync(
            await reviewer.PostAsJsonAsync($"{Base}/REQ-TEST-R22/questions", new { questions = Enumerable.Repeat("Why?", 11).ToArray() }),
            HttpStatusCode.BadRequest,
            "invalid_details");

        using var asked = await reviewer.PostAsJsonAsync($"{Base}/REQ-TEST-R22/questions", new { questions = OneQuestion });
        Assert.Equal(HttpStatusCode.NoContent, asked.StatusCode);

        Assert.Equal("InfoRequested", await StatusOfAsync("REQ-TEST-R22"));
        var payload = await ScalarAsync<string>(
            "SELECT Payload FROM core.OutboxMessages WHERE AggregateId = 'REQ-TEST-R22' AND EventType = 'RegistrationInfoRequested';");
        Assert.Equal(OnboardingScenario.Applicant, JsonDocument.Parse(payload).RootElement.GetProperty("applicantUserId").GetGuid());

        var question = Assert.Single((await JsonAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R22"))).GetProperty("application").GetProperty("questions").EnumerateArray());
        Assert.Equal(OneQuestion[0], question.GetProperty("question").GetString());
    }

    // ------------------------------------------------------------------ rejection

    [Fact]
    public async Task ARejectionNeedsAKnownReason_IsAuditedAndTexted_AndIsFinal()
    {
        await UnderReviewAsync("REQ-TEST-R30", "BUD/R/30");
        var admin = await AdminAsync();

        await AssertRefusedAsync(await admin.PostAsJsonAsync($"{Base}/REQ-TEST-R30/reject", new { reason = "BECAUSE" }), HttpStatusCode.BadRequest, "reject_reason_unknown");
        await AssertRefusedAsync(await admin.PostAsJsonAsync($"{Base}/REQ-TEST-R30/reject", new { note = "No reason" }), HttpStatusCode.BadRequest, "invalid_details");
        Assert.Equal("UnderReview", await StatusOfAsync("REQ-TEST-R30"));

        using var rejected = await admin.PostAsJsonAsync($"{Base}/REQ-TEST-R30/reject", new { reason = "not_eligible", note = "Not a religious or care institution." });
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);

        Assert.Equal("Rejected", await StatusOfAsync("REQ-TEST-R30"));
        Assert.Equal("{\"status\":\"Rejected\",\"reason\":\"NOT_ELIGIBLE\"}", await ScalarAsync<string>(
            "SELECT NewValues FROM audit.AuditLogs WHERE EntityId = 'REQ-TEST-R30' AND Action = 'Reject' AND UserId = @admin;", new { admin = Admin }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateId = 'REQ-TEST-R30' AND EventType = 'RegistrationRejected';"));

        var view = (await JsonAsync(await Reviewer().GetAsync($"{Base}/REQ-TEST-R30"))).GetProperty("application");
        Assert.Equal(("NOT_ELIGIBLE", "Not a religious or care institution."), (view.GetProperty("rejectReason").GetString(), view.GetProperty("decisionNote").GetString()));

        await AssertRefusedAsync(await admin.PostAsync($"{Base}/REQ-TEST-R30/approve", null), HttpStatusCode.Conflict, "invalid_transition");
        await AssertRefusedAsync(await admin.PostAsJsonAsync($"{Base}/REQ-TEST-R30/reject", new { reason = "OTHER" }), HttpStatusCode.Conflict, "invalid_transition");
    }

    [Fact]
    public async Task ARejectionWaitsForADecisionInProgress()
    {
        // An approval holds the decision lock while it builds the institution; a rejection that did
        // not take the same lock could land in the middle of it.
        await UnderReviewAsync("REQ-TEST-R31", "BUD/R/31");
        var admin = await AdminAsync();

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "EXEC sys.sp_getapplock @Resource = N'onboarding:decision', @LockMode = N'Exclusive', @LockOwner = N'Transaction';",
            transaction: hold);

        var reject = Task.Run(() => admin.PostAsJsonAsync($"{Base}/REQ-TEST-R31/reject", new { reason = "DUPLICATE" }));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        await using (var watcher = new SqlConnection(database.ConnectionString))
        {
            while (await watcher.ExecuteScalarAsync<int>(
                       "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID();") < 1)
            {
                Assert.True(DateTime.UtcNow < deadline, "The rejection never waited for the decision lock.");
                await Task.Delay(50);
            }
        }

        Assert.Equal("UnderReview", await StatusOfAsync("REQ-TEST-R31"));
        await hold.RollbackAsync();

        using var rejected = await reject;
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Equal("Rejected", await StatusOfAsync("REQ-TEST-R31"));
    }

    // ------------------------------------------------------------------ who may act

    [Fact]
    public async Task NoOneReviewsOrDecidesTheirOwnApplication()
    {
        await UnderReviewAsync("REQ-TEST-R40", "BUD/R/40");
        await _scenario.SubmittedAsync("REQ-TEST-R41", "BUD/R/41");
        await GrantRoleAsync(OnboardingScenario.Applicant, "PLATFORM_ADMIN");

        var (applicant, tokenId) = PlatformClient(OnboardingScenario.Applicant);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(OnboardingScenario.Applicant, tokenId), 1, TimeSpan.FromMinutes(15));

        await AssertRefusedAsync(await applicant.PostAsync($"{Base}/REQ-TEST-R41/start-review", null), HttpStatusCode.Forbidden, "own_application");
        await AssertRefusedAsync(await applicant.PostAsJsonAsync($"{Base}/REQ-TEST-R40/questions", new { questions = OneQuestion }), HttpStatusCode.Forbidden, "own_application");
        await AssertRefusedAsync(await applicant.PostAsJsonAsync($"{Base}/REQ-TEST-R40/reject", new { reason = "OTHER" }), HttpStatusCode.Forbidden, "own_application");
        await AssertRefusedAsync(await applicant.PostAsync($"{Base}/REQ-TEST-R40/approve", null), HttpStatusCode.Forbidden, "own_application");

        Assert.Equal(("UnderReview", "Submitted"), (await StatusOfAsync("REQ-TEST-R40"), await StatusOfAsync("REQ-TEST-R41")));
    }

    // ------------------------------------------------------------------ the race, forced

    [Fact]
    public async Task TwoReviewersAtOnce_OneWinsAndTheOtherIsToldStale()
    {
        await UnderReviewAsync("REQ-TEST-R50", "BUD/R/50");
        var codes = await MandatoryCodesAsync();
        var reviewer = Reviewer();

        // Hold the application's row so both reads queue behind it and then see the same version.
        // That needs locking reads (READ_COMMITTED_SNAPSHOT off, as locally). Azure SQL turns it on:
        // there the reads would not queue, and the RowVersion would still let no update be lost.
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await holder.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;");
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("UPDATE org.RegistrationRequests SET UpdatedAt = UpdatedAt WHERE RequestNo = 'REQ-TEST-R50';", transaction: hold);

        var both = codes.Take(2)
            .Select(code => Task.Run(() => reviewer.PutAsJsonAsync($"{Base}/REQ-TEST-R50/documents/{code}/verification", new { verified = true })))
            .ToArray();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        await using (var watcher = new SqlConnection(database.ConnectionString))
        {
            while (await watcher.ExecuteScalarAsync<int>(
                       """
                       SELECT COUNT(*) FROM sys.dm_tran_locks AS l
                       JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                       WHERE l.resource_database_id = DB_ID() AND l.request_status = 'WAIT' AND p.object_id = OBJECT_ID(N'org.RegistrationRequests');
                       """) < 2)
            {
                Assert.True(DateTime.UtcNow < deadline, "The two reviewers never queued on the application's row.");
                await Task.Delay(50);
            }
        }

        await hold.CommitAsync();
        var responses = await Task.WhenAll(both);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        await AssertRefusedAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.Conflict, "stale");
    }

    // ------------------------------------------------------------------ documents

    [Fact]
    public async Task TheReviewerDownloadsAnUpload_ButNotADraftsUpload()
    {
        await _scenario.SubmittedAsync("REQ-TEST-R60", "BUD/R/60");
        await _scenario.SubmittedAsync("REQ-TEST-R61", "BUD/R/61", submit: false);
        var code = (await MandatoryCodesAsync())[0];

        // The scenario's files have no bytes behind them; give both uploads real ones.
        var bytes = "%PDF-1.7\n%%EOF\n"u8.ToArray();
        var saved = await _host.Services.GetRequiredService<IFileStorage>().SaveAsync(new MemoryStream(bytes), 1024);
        await ExecuteAsync(
            """
            EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;
            UPDATE f SET StorageKey = @key FROM core.Files f
            JOIN org.RegistrationRequestDocuments d ON d.FileId = f.Id
            JOIN org.RegistrationRequests r ON r.Id = d.RequestId
            JOIN org.DocumentTypes t ON t.Id = d.DocumentTypeId
            WHERE r.RequestNo IN ('REQ-TEST-R60', 'REQ-TEST-R61') AND t.Code = @code;
            """,
            new { key = saved!.StorageKey, code });
        var reviewer = Reviewer();

        using var download = await reviewer.GetAsync($"{Base}/REQ-TEST-R60/documents/{code}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        await AssertRefusedAsync(await reviewer.GetAsync($"{Base}/REQ-TEST-R61/documents/{code}/file"), HttpStatusCode.NotFound, "document_not_found");
    }
}

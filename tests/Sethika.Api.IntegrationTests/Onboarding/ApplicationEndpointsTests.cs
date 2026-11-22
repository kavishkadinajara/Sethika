using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Access;

namespace Sethika.Api.IntegrationTests.Onboarding;

/// <summary>
/// Plan 02, 5.3: the applicant's endpoints over HTTP, against SQL Server, Redis and a real file
/// root. Each applicant is a donor made for the test, signed in by a token in a personal session.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ApplicationEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "application-endpoint-test-signing-key-32-bytes!!";
    private const string Base = "/api/onboarding/applications";

    private static readonly string[] Unasked = ["An answer to nothing"];

    private static readonly byte[] Pdf = "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n"u8.ToArray();

    private readonly string _files = Path.Combine(Path.GetTempPath(), "sethika-app-files-" + Guid.NewGuid().ToString("N"));
    private readonly List<Guid> _donors = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        Directory.CreateDirectory(_files);

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
        await database.SeedAsync();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;
            DELETE FROM core.OutboxMessages WHERE AggregateType = 'RegistrationRequest'
                AND JSON_VALUE(Payload, '$.applicantUserId') IN (SELECT CAST(Id AS NVARCHAR(36)) FROM auth.Users WHERE Id IN @donors);
            DELETE FROM core.Files WHERE UploadedBy IN @donors;
            DELETE FROM auth.UserRoles WHERE UserId IN @donors;
            DELETE FROM auth.Users WHERE Id IN @donors;
            """,
            new { donors = _donors });

        Directory.Delete(_files, recursive: true);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A signed-up donor: the DONOR role, nothing else, and a client carrying their token.</summary>
    private async Task<(Guid Id, HttpClient Client)> DonorAsync(int? institutionId = null)
    {
        var donor = Guid.CreateVersion7();
        _donors.Add(donor);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status)
            VALUES (@donor, 'Donor', N'Applicant Donor', @phone, 1, 'en', 'Active');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @donor, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { donor, phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture) });

        var tokens = JwtTokenServiceTests.CreateService(SigningKey, TimeProvider.System)
            .Issue(new TokenSubject(donor, institutionId, IsPlatform: false));

        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return (donor, client);
    }

    private static Dictionary<string, object?> Details(string registrationNo = "BUD/GAL/1952/77", object? overrides = null)
    {
        var details = new Dictionary<string, object?>
        {
            ["institutionType"] = "TEMPLE",
            ["proposedName"] = "Sri Sudharmaramaya",
            ["nameSi"] = "ශ්‍රී සුධර්මාරාමය",
            ["officialRegistrationNo"] = registrationNo,
            ["registeringAuthority"] = "Department of Buddhist Affairs",
            ["registrationDate"] = "1952-04-01",
            ["addressLine1"] = "12 Temple Road",
            ["city"] = "Galle",
            ["district"] = "GLL",
            ["latitude"] = 6.0535m,
            ["longitude"] = 80.221m,
            ["officePhone"] = "091 223 4567",
            ["applicantFullName"] = "Kamal Perera",
            ["applicantNic"] = "199012345678",
            ["applicantDesignation"] = "Secretary",
            ["applicantPhone"] = "077 100 0001",
        };

        if (overrides is not null)
        {
            foreach (var property in overrides.GetType().GetProperties())
            {
                details[property.Name] = property.GetValue(overrides);
            }
        }

        return details;
    }

    private static readonly object Bearers = new
    {
        officeBearers = new[]
        {
            new { fullName = "Chief Trustee", nic = "561234567v", designation = "Trustee", phone = "0771234567", email = (string?)null, isSignatory = true },
        },
    };

    private static async Task<string> CreateAsync(HttpClient client, string registrationNo = "BUD/GAL/1952/77")
    {
        using var response = await client.PostAsJsonAsync(Base, Details(registrationNo));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("requestNo").GetString()!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string requestNo, string documentType, byte[] content, string fileName = "certificate.pdf")
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(content), "file", fileName },
            { new StringContent("REF-1952-77"), "referenceNo" },
            { new StringContent("1952-04-01"), "issuedDate" },
        };

        return await client.PutAsync($"{Base}/{requestNo}/documents/{documentType}", form);
    }

    private static async Task<string[]> MandatoryDocumentsAsync(HttpClient client)
    {
        var lookups = await JsonAsync(await client.GetAsync($"{Base}/lookups?language=en"));
        return [.. lookups.GetProperty("documents").EnumerateArray()
            .Where(d => d.GetProperty("institutionType").GetString() == "TEMPLE" && d.GetProperty("isMandatory").GetBoolean())
            .Select(d => d.GetProperty("code").GetString()!)];
    }

    /// <summary>A draft with its office bearer and every mandatory document: one submit from Submitted.</summary>
    private static async Task<string> CompleteDraftAsync(HttpClient client, string registrationNo = "BUD/GAL/1952/77")
    {
        var requestNo = await CreateAsync(client, registrationNo);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Base}/{requestNo}/office-bearers", Bearers)).StatusCode);

        foreach (var type in await MandatoryDocumentsAsync(client))
        {
            using var uploaded = await UploadAsync(client, requestNo, type, Pdf);
            Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);
        }

        return requestNo;
    }

    private static async Task<string> DeclarationHashAsync(HttpClient client, string language = "si")
    {
        var lookups = await JsonAsync(await client.GetAsync($"{Base}/lookups?language={language}"));
        var declaration = lookups.GetProperty("declaration");
        Assert.Equal(language, declaration.GetProperty("language").GetString());
        return declaration.GetProperty("contentSha256").GetString()!;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string requestNo, string hash) =>
        await client.PostAsJsonAsync($"{Base}/{requestNo}/submit", new { declarationSha256 = hash });

    /// <summary>The body as JSON; a body that is not JSON - an unhandled error's page - fails with the body in the message.</summary>
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            Assert.Fail($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body[..Math.Min(body.Length, 1500)]}");
            throw;
        }
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString();

    private async Task<T> PlatformScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;");
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException(sql);
    }

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public async Task AnApplicantStartsFillsInUploadsAndSubmits()
    {
        var (_, client) = await DonorAsync();

        var requestNo = await CompleteDraftAsync(client);
        Assert.Matches("^REQ-[0-9]{4}-[0-9]{6}$", requestNo);

        using var submitted = await SubmitAsync(client, requestNo, await DeclarationHashAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, submitted.StatusCode);

        var view = await JsonAsync(await client.GetAsync($"{Base}/{requestNo}"));
        Assert.Equal("Submitted", view.GetProperty("status").GetString());
        Assert.Equal("+94912234567", view.GetProperty("officePhone").GetString());
        Assert.Equal("ශ්‍රී සුධර්මාරාමය", view.GetProperty("nameSi").GetString());
        Assert.Equal("1952-04-01", view.GetProperty("registrationDate").GetString());
        Assert.Equal("561234567V", view.GetProperty("officeBearers")[0].GetProperty("nic").GetString());
        Assert.Equal(4, view.GetProperty("documents").GetArrayLength());
        Assert.All(view.GetProperty("documents").EnumerateArray(), d => Assert.Equal("Pending", d.GetProperty("verificationStatus").GetString()));
        Assert.Equal(["Draft", "Submitted"], view.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("toStatus").GetString()));

        var mine = await JsonAsync(await client.GetAsync(Base));
        Assert.Equal(requestNo, Assert.Single(mine.EnumerateArray()).GetProperty("requestNo").GetString());

        Assert.Equal(1, await PlatformScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'RegistrationSubmitted' AND AggregateId = @requestNo;", new { requestNo }));
        Assert.Equal(4, Directory.GetFiles(_files, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task AnUploadComesBackAsAnAttachmentWithItsOwnBytes()
    {
        var (_, client) = await DonorAsync();
        var requestNo = await CreateAsync(client);
        using (var uploaded = await UploadAsync(client, requestNo, "REGISTRATION_CERTIFICATE", Pdf, "Society certificate.pdf"))
        {
            Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);
        }

        using var download = await client.GetAsync($"{Base}/{requestNo}/documents/registration_certificate/file");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Society certificate.pdf", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task DetailsThatAreMissingOrMalformedAreNamedField_ByField()
    {
        var (_, client) = await DonorAsync();

        using var response = await client.PostAsJsonAsync(Base, Details(overrides: new
        {
            applicantNic = "12345",
            latitude = 51.5m,
            district = "LONDON",
            officePhone = "not a phone",
            website = "javascript:alert(1)",
            proposedName = "  ",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(
            ["applicantNic", "district", "latitude", "officePhone", "proposedName", "website"],
            body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).Order());
    }

    [Fact]
    public async Task ARenamedExecutableIsRefusedAndNothingIsKept()
    {
        var (_, client) = await DonorAsync();
        var requestNo = await CreateAsync(client);

        using var response = await UploadAsync(client, requestNo, "REGISTRATION_CERTIFICATE", "MZ\x90\0\x03\0\0\0 not a pdf"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file_content_mismatch", await CodeOf(response));
        Assert.Empty(Directory.GetFiles(_files, "*", SearchOption.AllDirectories));
        Assert.Equal(0, await PlatformScalarAsync<int>(
            "SELECT COUNT(*) FROM core.Files WHERE UploadedBy IN @donors;", new { donors = _donors }));
    }

    [Fact]
    public async Task ADocumentTypeTheInstitutionTypeDoesNotTakeIsRefused()
    {
        // A real, active document type - just not one temples provide.
        var (_, client) = await DonorAsync();
        var requestNo = await CreateAsync(client);
        await using var connection = new SqlConnection(database.ConnectionString);

        // If the check is ever broken the upload lands, and an upload pins the type: remove both,
        // or the type outlives this test and the next run's INSERT fails for the wrong reason.
        const string Cleanup = """
            DELETE d FROM org.RegistrationRequestDocuments d
            JOIN org.DocumentTypes t ON t.Id = d.DocumentTypeId WHERE t.Code = 'TEST_NOT_FOR_TEMPLES';
            DELETE FROM org.DocumentTypes WHERE Code = 'TEST_NOT_FOR_TEMPLES';
            """;
        await connection.ExecuteAsync(Cleanup);
        await connection.ExecuteAsync("INSERT org.DocumentTypes (Code, Name) VALUES ('TEST_NOT_FOR_TEMPLES', N'Test type');");

        try
        {
            using var response = await UploadAsync(client, requestNo, "TEST_NOT_FOR_TEMPLES", Pdf);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("document_type_not_allowed", await CodeOf(response));
        }
        finally
        {
            await connection.ExecuteAsync(Cleanup);
        }
    }

    [Fact]
    public async Task AFileStoredForASaveThatFailsIsDeletedAgain()
    {
        // The bytes are written before the transaction; a CHECK planted on core.Files fails the save
        // after them, and the file must not stay behind on disk.
        var (_, client) = await DonorAsync();
        var requestNo = await CreateAsync(client);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("ALTER TABLE core.Files WITH NOCHECK ADD CONSTRAINT CK_Test_NoBoom CHECK (OriginalName <> N'boom.pdf');");

        try
        {
            using var response = await UploadAsync(client, requestNo, "REGISTRATION_CERTIFICATE", Pdf, "boom.pdf");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await connection.ExecuteAsync("ALTER TABLE core.Files DROP CONSTRAINT CK_Test_NoBoom;");
        }

        Assert.Empty(Directory.GetFiles(_files, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SubmittingNeedsTheDocumentsAndACurrentDeclaration()
    {
        var (_, client) = await DonorAsync();
        var requestNo = await CreateAsync(client);
        await client.PutAsJsonAsync($"{Base}/{requestNo}/office-bearers", Bearers);

        using (var missing = await SubmitAsync(client, requestNo, await DeclarationHashAsync(client)))
        {
            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
            Assert.Equal("documents_missing", await CodeOf(missing));
        }

        using var wrong = await SubmitAsync(client, requestNo, new string('0', 64));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("declaration_required", await CodeOf(wrong));
    }

    [Fact]
    public async Task ASubmittedApplicationCannotBeEditedButCanBeWithdrawn()
    {
        var (_, client) = await DonorAsync();
        var requestNo = await CompleteDraftAsync(client);
        await SubmitAsync(client, requestNo, await DeclarationHashAsync(client));

        using (var edit = await client.PutAsJsonAsync($"{Base}/{requestNo}", Details()))
        {
            Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
            Assert.Equal("application_not_editable", await CodeOf(edit));
        }

        using (var answer = await client.PostAsJsonAsync($"{Base}/{requestNo}/answers", new { answers = Unasked }))
        {
            Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
            Assert.Equal("invalid_transition", await CodeOf(answer));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Base}/{requestNo}/withdraw", null)).StatusCode);
        Assert.Equal("Withdrawn", (await JsonAsync(await client.GetAsync($"{Base}/{requestNo}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ARegistrationNumberAlreadyInALiveApplicationIsRefused_WhoeverItBelongsTo()
    {
        // The other application is invisible to this applicant under RLS; only the index sees the clash.
        var (_, first) = await DonorAsync();
        var (_, second) = await DonorAsync();
        var mine = await CompleteDraftAsync(first);
        var theirs = await CompleteDraftAsync(second);
        var hash = await DeclarationHashAsync(first);

        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(first, mine, hash)).StatusCode);

        using var clash = await SubmitAsync(second, theirs, hash);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("registration_no_in_use", await CodeOf(clash));
    }

    [Fact]
    public async Task ANumberChangedWhileAnsweringAQuestionToALiveOnesIsRefused_AndNothingChanges()
    {
        // QA DEF-004: InfoRequested is in the index's filter, so the clash is met on the edit, not the submit.
        var (_, first) = await DonorAsync();
        var (_, second) = await DonorAsync();
        var hash = await DeclarationHashAsync(first);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(first, await CompleteDraftAsync(first), hash)).StatusCode);
        var theirs = await CompleteDraftAsync(second, "BUD/GAL/1960/12");
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(second, theirs, hash)).StatusCode);

        // A reviewer's question, as the review handler leaves it (the reviewer's own flow is RegistrationReviewEndpointsTests').
        await PlatformScalarAsync<int>(
            "UPDATE org.RegistrationRequests SET Status = 'InfoRequested' WHERE RequestNo = @requestNo; SELECT @@ROWCOUNT;",
            new { requestNo = theirs });

        using (var clash = await second.PutAsJsonAsync($"{Base}/{theirs}", Details("BUD/GAL/1952/77")))
        {
            Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
            Assert.Equal("registration_no_in_use", await CodeOf(clash));
        }

        var view = await JsonAsync(await second.GetAsync($"{Base}/{theirs}"));
        Assert.Equal("BUD/GAL/1960/12", view.GetProperty("officialRegistrationNo").GetString());
        Assert.Equal("InfoRequested", view.GetProperty("status").GetString());

        // A free number is still taken in the same state.
        using var free = await second.PutAsJsonAsync($"{Base}/{theirs}", Details("BUD/GAL/1960/13"));
        Assert.Equal(HttpStatusCode.NoContent, free.StatusCode);
    }

    // ------------------------------------------------------------------ whose application

    [Fact]
    public async Task AnotherApplicantFindsNothingAtEveryAddress()
    {
        var (_, owner) = await DonorAsync();
        var (_, other) = await DonorAsync();
        var requestNo = await CreateAsync(owner);
        using (var uploaded = await UploadAsync(owner, requestNo, "REGISTRATION_CERTIFICATE", Pdf))
        {
            Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);
        }

        var hash = await DeclarationHashAsync(other);
        HttpResponseMessage[] attempts =
        [
            await other.GetAsync($"{Base}/{requestNo}"),
            await other.PutAsJsonAsync($"{Base}/{requestNo}", Details()),
            await other.PutAsJsonAsync($"{Base}/{requestNo}/office-bearers", Bearers),
            await UploadAsync(other, requestNo, "REGISTRATION_CERTIFICATE", Pdf),
            await other.GetAsync($"{Base}/{requestNo}/documents/REGISTRATION_CERTIFICATE/file"),
            await SubmitAsync(other, requestNo, hash),
            await other.PostAsJsonAsync($"{Base}/{requestNo}/answers", new { answers = Unasked }),
            await other.PostAsync($"{Base}/{requestNo}/withdraw", null),
        ];

        Assert.All(attempts, a => Assert.Equal(HttpStatusCode.NotFound, a.StatusCode));
        Assert.Empty((await JsonAsync(await other.GetAsync(Base))).EnumerateArray());
        Assert.Equal("Draft", (await JsonAsync(await owner.GetAsync($"{Base}/{requestNo}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task TheApplicantsPermissionsHoldOnlyInAPersonalSession()
    {
        // Plan 02, B1: the DONOR role is global, and a session switched into an institution reads
        // that institution's grants alone.
        var (_, client) = await DonorAsync(institutionId: database.AlphaInstitutionId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Base, Details())).StatusCode);
    }

    // ------------------------------------------------------------------ rate limits

    [Fact]
    public async Task CreatesAreRateLimitedPerApplicant()
    {
        // Malformed creates still spend the budget: the limit stands in front of the handler, so
        // these never take a number or reach the limit of three.
        var (_, client) = await DonorAsync();
        for (var i = 0; i < 10; i++)
        {
            using var refused = await client.PostAsJsonAsync(Base, new { });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(Base, new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        var (_, someoneElse) = await DonorAsync();
        using var unaffected = await someoneElse.PostAsJsonAsync(Base, new { });
        Assert.Equal(HttpStatusCode.BadRequest, unaffected.StatusCode);
    }

    [Fact]
    public async Task UploadsAreRateLimitedPerApplicant()
    {
        var (_, client) = await DonorAsync();
        for (var i = 0; i < 60; i++)
        {
            using var missing = await UploadAsync(client, "REQ-0000-000000", "REGISTRATION_CERTIFICATE", Pdf);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var limited = await UploadAsync(client, "REQ-0000-000000", "REGISTRATION_CERTIFICATE", Pdf);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    // ------------------------------------------------------------------ the limit, forced

    [Fact]
    public async Task OnlyThreeOpenApplications_EvenWhenTheFourthAndFifthArriveTogether()
    {
        var (donor, client) = await DonorAsync();
        await CreateAsync(client, "BUD/GAL/1/1");
        await CreateAsync(client, "BUD/GAL/1/2");

        // Hold the applicant's row, start three creates, let them go at once: one fits under three.
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT Id FROM auth.Users WITH (UPDLOCK) WHERE Id = @donor;", new { donor }, hold);

        var creates = Enumerable.Range(3, 3)
            .Select(n => Task.Run(() => client.PostAsJsonAsync(Base, Details($"BUD/GAL/1/{n}"))))
            .ToArray();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        await using (var watcher = new SqlConnection(database.ConnectionString))
        {
            while (await watcher.ExecuteScalarAsync<int>(
                       """
                       SELECT COUNT(*) FROM sys.dm_tran_locks AS l
                       JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                       WHERE l.resource_database_id = DB_ID() AND l.request_status = 'WAIT' AND p.object_id = OBJECT_ID(N'auth.Users');
                       """) < 3)
            {
                Assert.True(DateTime.UtcNow < deadline, "The creates never queued on the applicant's row.");
                await Task.Delay(50);
            }
        }

        await hold.RollbackAsync();
        var responses = await Task.WhenAll(creates);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, (await JsonAsync(await client.GetAsync(Base))).GetArrayLength());
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Giving.Donations;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Giving;

/// <summary>
/// Plan 09, D.2 over HTTP, against real SQL Server and Redis: needs (M11), the public board (M12), pledging
/// with the counted-inventory UPDATE and its forced race (M13), the platform scope and RLS (M14), and
/// receiving into in-kind receipts (M15), with the rules decided while building (M29, M30).
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles: the owner every GIVING.NEEDS permission, staff VIEW.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class NeedEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "need-test-signing-key-at-least-32-bytes-long!";
    private const string Needs = "/api/institution/needs";
    private const string Pledges = "/api/institution/pledges";
    private const string Platform = "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    private Guid _alphaOwner;
    private Guid _alphaManager;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);

        await ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            SELECT i.Id, 'Institution', t.RoleLevelId, t.Code, t.Name, 0
            FROM auth.Roles t CROSS JOIN org.Institutions i
            WHERE t.IsTemplate = 1 AND t.InstitutionId IS NULL AND i.Id IN (@alpha, @beta);

            INSERT auth.RolePermissions (RoleId, PermissionId)
            SELECT r.Id, tp.PermissionId
            FROM auth.Roles r
            JOIN auth.Roles t            ON t.Code = r.Code AND t.IsTemplate = 1 AND t.InstitutionId IS NULL
            JOIN auth.RolePermissions tp ON tp.RoleId = t.Id
            WHERE r.InstitutionId IN (@alpha, @beta);

            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT i.Id, m.Id, 1, 'Manual', @changedBy
            FROM auth.Modules m CROSS JOIN org.Institutions i
            WHERE i.Id IN (@alpha, @beta) AND m.Code = 'GIVING';

            UPDATE org.Institutions SET Status = 'Active', IsPubliclyListed = 1 WHERE Id IN (@alpha, @beta);
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _alphaOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");
        _alphaManager = await MemberAsync(database.AlphaInstitutionId, "MANAGER");
        _alphaStaff = await MemberAsync(database.AlphaInstitutionId, "STAFF");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms)));
        });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM notify.Notifications WHERE Recipient IN @phones;
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users, phones = _phones });
        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((phoneNumber, message));
            return Task.CompletedTask;
        }
    }

    private async Task<SqlConnection> ConnectionAsync(string stamp)
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        if (stamp.Length > 0)
        {
            await connection.ExecuteAsync(stamp);
        }

        return connection;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await ConnectionAsync(Platform);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await ConnectionAsync(Platform);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private async Task<Guid> UserAsync(string userType, string name = "Giving Test", bool confirmed = true)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, @name, @phone, @confirmed, 'en', 'Active', 'stamp');
            """,
            new { id, userType, name, phone, confirmed });

        _users.Add(id);
        return id;
    }

    private async Task<Guid> MemberAsync(int institutionId, string role)
    {
        var id = await UserAsync("InstitutionStaff");
        await ExecuteAsync(
            """
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @institutionId AND r.Code = @role;
            """,
            new { id, institutionId, role });

        return id;
    }

    private async Task<Guid> DonorAsync(string name = "Nimal Donor", bool confirmed = true)
    {
        var id = await UserAsync("Donor", name, confirmed);
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;",
            new { id });
        return id;
    }

    private (HttpClient Client, string TokenId) ClientFor(Guid userId, int? institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return (client, tokens.Access.JwtId);
    }

    private HttpClient Alpha(Guid member) => ClientFor(member, database.AlphaInstitutionId).Client;

    private HttpClient Beta(Guid member) => ClientFor(member, database.BetaInstitutionId).Client;

    private HttpClient Personal(Guid user) => ClientFor(user, null).Client;

    private HttpClient Anonymous() => _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<HttpClient> SteppedUpAlphaAsync(Guid member)
    {
        var (client, tokenId) = ClientFor(member, database.AlphaInstitutionId);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(member, tokenId), 1, TimeSpan.FromMinutes(15));
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            return await client.SendAsync(request);
        }
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonElement.Parse(text).Clone();
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        Assert.Equal(code, JsonElement.Parse(body).GetProperty("code").GetString());
    }

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        var body = await BodyOf(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(fields, body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    private static async Task AssertOkAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Details(decimal quantity = 50m, string title = "Exercise books", string priority = "Normal", string category = "STATIONERY", string unit = "PIECES", DateOnly? deadline = null, string? version = null) =>
        new()
        {
            ["title"] = title,
            ["description"] = "For the Grade 5 class.",
            ["category"] = category,
            ["unit"] = unit,
            ["quantity"] = quantity,
            ["priority"] = priority,
            ["deadline"] = deadline is { } day ? Iso(day) : null,
            ["version"] = version,
        };

    private static async Task<Guid> DraftAsync(HttpClient client, Dictionary<string, object?>? details = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, Needs, details ?? Details());
        await AssertOkAsync(response);
        return (await BodyOf(response)).GetProperty("publicId").GetGuid();
    }

    private async Task<string> VersionAsync(Guid need) =>
        Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Needs WHERE PublicId = @need;", new { need }));

    private async Task<string> PledgeVersionAsync(Guid pledge) =>
        Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Pledges WHERE PublicId = @pledge;", new { pledge }));

    /// <summary>A need of Alpha's, on the board.</summary>
    private async Task<Guid> OpenNeedAsync(decimal quantity = 50m, Dictionary<string, object?>? details = null)
    {
        var need = await DraftAsync(Alpha(_alphaOwner), details ?? Details(quantity));
        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/open", new { version = await VersionAsync(need) }));
        return need;
    }

    private Task<HttpResponseMessage> TryPledgeAsync(Guid donor, Guid need, decimal quantity, DateOnly? expected = null) =>
        SendAsync(Personal(donor), HttpMethod.Post, $"/api/needs/{need}/pledges", new { quantity, expectedDate = expected is { } day ? Iso(day) : null });

    private async Task<Guid> PledgeAsync(Guid donor, Guid need, decimal quantity)
    {
        var response = await TryPledgeAsync(donor, need, quantity);
        await AssertOkAsync(response);
        return (await BodyOf(response)).GetProperty("publicId").GetGuid();
    }

    private async Task<HttpResponseMessage> ReceiveAsync(Guid pledge, decimal quantity, HttpClient? client = null) =>
        await SendAsync(client ?? Alpha(_alphaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/receive", new { quantity, version = await PledgeVersionAsync(pledge) });

    private sealed record Counters(decimal Needed, decimal Pledged, decimal Received, string Status);

    private async Task<Counters> CountersAsync(Guid need)
    {
        await using var connection = await ConnectionAsync(Platform);
        return await connection.QuerySingleAsync<Counters>(
            "SELECT QuantityNeeded AS Needed, QuantityPledged AS Pledged, QuantityReceived AS Received, Status AS Status FROM giving.Needs WHERE PublicId = @need;",
            new { need });
    }

    private static Dictionary<string, object?> ItemsFor(Guid? need, decimal? quantity) =>
        new()
        {
            ["kind"] = "InKind",
            ["items"] = "Books brought in",
            ["quantity"] = quantity,
            ["date"] = Iso(Today),
            ["donorName"] = "Walk-in Donor",
            ["need"] = need,
        };

    private async Task<(Guid PublicId, string ReceiptNo, string Version)> RecordAsync(object body)
    {
        var response = await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/donations", body);
        await AssertOkAsync(response);
        var json = await BodyOf(response);
        return (json.GetProperty("publicId").GetGuid(), json.GetProperty("receiptNo").GetString()!, json.GetProperty("version").GetString()!);
    }

    private async Task<string[]> BoardTitlesAsync(string query = "")
    {
        var response = await SendAsync(Anonymous(), HttpMethod.Get, "/api/needs" + query);
        await AssertOkAsync(response);
        return [.. (await BodyOf(response)).EnumerateArray().Select(n => n.GetProperty("title").GetString()!)];
    }

    // ------------------------------------------------------------------ needs (M11)

    [Fact]
    public async Task ADraftIsNobodys_UntilItIsOpened()
    {
        var need = await DraftAsync(Alpha(_alphaOwner), Details(title: "Rice for the hostel"));

        Assert.Empty(await BoardTitlesAsync());
        var list = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, Needs));
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal("Draft", row.GetProperty("status").GetString());
        Assert.Equal("STATIONERY", row.GetProperty("category").GetString());

        // A donor cannot pledge to a draft: it reads as nothing at all.
        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 1m), HttpStatusCode.NotFound, "need_not_found");

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/open", new { version = await VersionAsync(need) }));
        Assert.Equal(["Rice for the hostel"], await BoardTitlesAsync());
    }

    [Fact]
    public async Task EachPermissionGuardsItsEndpoint()
    {
        // Staff hold GIVING.NEEDS.VIEW only (M19); a donor holds none of it.
        var need = await OpenNeedAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, Needs)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Needs, Details())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Put, $"{Needs}/{need}", Details())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"{Needs}/{need}/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Personal(await DonorAsync()), HttpMethod.Get, Needs)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(Alpha(_alphaManager), HttpMethod.Post, Needs, Details())).StatusCode);
    }

    [Fact]
    public async Task ANeedsFormIsChecked()
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Needs, new
        {
            title = " ",
            category = "NOT_A_CATEGORY",
            unit = "CASH", // a real lookup code, of another list
            quantity = 0.001m,
            priority = "Huge",
            deadline = Iso(Today.AddDays(-1)),
        });

        await AssertFieldsAsync(response, "title", "category", "unit", "quantity", "priority", "deadline");
    }

    [Fact]
    public async Task QuantityNeverGoesBelowWhatIsSpokenFor_NorTheUnitChangesUnderAPledge()
    {
        var need = await OpenNeedAsync(50m);
        await PledgeAsync(await DonorAsync(), need, 20m);

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(19m, version: await VersionAsync(need))),
            HttpStatusCode.Conflict,
            "quantity_below_pledged");

        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(30m, unit: "KG", version: await VersionAsync(need))),
            "unit");

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(20m, version: await VersionAsync(need))));
        Assert.Equal(new Counters(20m, 20m, 0m, "Open"), await CountersAsync(need));
    }

    [Fact]
    public async Task AChangeOnAnOldReading_IsStale_BecauseAPledgeMovesTheVersion()
    {
        var need = await OpenNeedAsync(50m);
        var read = await VersionAsync(need);
        await PledgeAsync(await DonorAsync(), need, 40m);

        // Refused on what is true now, whichever version was sent.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/cancel", new { version = read }),
            HttpStatusCode.Conflict,
            "open_pledges");

        var fresh = await OpenNeedAsync(50m);
        var old = await VersionAsync(fresh);
        await PledgeAsync(await DonorAsync(), fresh, 10m);
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{fresh}", Details(30m, version: old)),
            HttpStatusCode.Conflict,
            "stale");
        Assert.Equal(50m, (await CountersAsync(fresh)).Needed);
    }

    [Fact]
    public async Task BroughtDownToWhatArrived_ANeedIsFulfilled()
    {
        var need = await OpenNeedAsync(50m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 20m);
        await AssertOkAsync(await ReceiveAsync(pledge, 20m));

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(20m, version: await VersionAsync(need))));

        Assert.Equal(new Counters(20m, 20m, 20m, "Fulfilled"), await CountersAsync(need));
    }

    [Fact]
    public async Task ClosingAndCancelling_WaitForOpenPledges_AndGiftsAreNeverCancelledAway()
    {
        var need = await OpenNeedAsync(50m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 20m);

        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }), HttpStatusCode.Conflict, "open_pledges");
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/cancel", new { version = await VersionAsync(need) }), HttpStatusCode.Conflict, "open_pledges");

        await AssertOkAsync(await ReceiveAsync(pledge, 20m));
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/cancel", new { version = await VersionAsync(need) }), HttpStatusCode.Conflict, "need_has_gifts");

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }));
        Assert.Equal("Closed", (await CountersAsync(need)).Status);
        Assert.Empty(await BoardTitlesAsync());

        // A settled need is history.
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(60m, version: await VersionAsync(need))), HttpStatusCode.Conflict, "need_wrong_status");
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/open", new { version = await VersionAsync(need) }), HttpStatusCode.Conflict, "need_wrong_status");
    }

    [Fact]
    public async Task AnotherInstitutionsNeed_ReadsAsNotFound()
    {
        var need = await OpenNeedAsync();
        var version = await VersionAsync(need);

        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Put, $"{Needs}/{need}", Details(version: version)), HttpStatusCode.NotFound, "need_not_found");
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version }), HttpStatusCode.NotFound, "need_not_found");
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Get, $"{Needs}/{need}/pledges"), HttpStatusCode.NotFound, "need_not_found");
    }

    // ------------------------------------------------------------------ the public board (M12)

    [Fact]
    public async Task TheBoard_IsAnonymous_UrgentFirst_ThenTheNearestDeadline()
    {
        await OpenNeedAsync(details: Details(title: "No deadline"));
        await OpenNeedAsync(details: Details(title: "Far", deadline: Today.AddDays(30)));
        await OpenNeedAsync(details: Details(title: "Near", deadline: Today.AddDays(3)));
        await OpenNeedAsync(details: Details(title: "Whenever", priority: "Low"));
        await OpenNeedAsync(details: Details(title: "Urgent, late", priority: "Urgent", deadline: Today.AddDays(40)));

        Assert.Equal(["Urgent, late", "Near", "Far", "No deadline", "Whenever"], await BoardTitlesAsync());
    }

    [Fact]
    public async Task TheBoard_Filters_AndShowsNobodyWhoPledged()
    {
        await OpenNeedAsync(details: Details(title: "Books"));
        var rice = await OpenNeedAsync(details: Details(title: "Rice", category: "DRY_RATIONS", unit: "KG"));
        await PledgeAsync(await DonorAsync("Secret Donor"), rice, 5m);

        var district = await ScalarAsync<string>("SELECT d.Code FROM core.Districts d JOIN org.Institutions i ON i.DistrictId = d.Id WHERE i.Id = @id;", new { id = database.AlphaInstitutionId });
        var type = await ScalarAsync<string>("SELECT t.Code FROM org.InstitutionTypes t JOIN org.Institutions i ON i.InstitutionTypeId = t.Id WHERE i.Id = @id;", new { id = database.AlphaInstitutionId });

        Assert.Equal(["Rice"], await BoardTitlesAsync("?category=DRY_RATIONS"));
        Assert.Equal(2, (await BoardTitlesAsync($"?district={district}&type={type}")).Length);
        Assert.Empty(await BoardTitlesAsync("?district=NOWHERE"));
        Assert.Empty(await BoardTitlesAsync("?type=NOTHING"));
        Assert.Equal(2, (await BoardTitlesAsync("?institution=alpha-temple")).Length);

        var text = await (await SendAsync(Anonymous(), HttpMethod.Get, "/api/needs")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret Donor", text, StringComparison.Ordinal);
        Assert.Contains("alpha-temple", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"quantityPledged\":5", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnlistedInstitution_IsOffTheBoard_ButOnItsOwnPage_AndASuspendedOneOnNeither()
    {
        await OpenNeedAsync(details: Details(title: "Quiet need"));
        await ExecuteAsync("UPDATE org.Institutions SET IsPubliclyListed = 0 WHERE Id = @id;", new { id = database.AlphaInstitutionId });

        Assert.Empty(await BoardTitlesAsync());
        Assert.Equal(["Quiet need"], await BoardTitlesAsync("?institution=alpha-temple"));

        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Id = @id;", new { id = database.AlphaInstitutionId });
        Assert.Empty(await BoardTitlesAsync("?institution=alpha-temple"));
    }

    [Fact]
    public async Task APassedDeadline_TakesItOffTheBoard_AndRefusesPledges()
    {
        var need = await OpenNeedAsync(details: Details(title: "Late", deadline: Today));
        await ExecuteAsync("UPDATE giving.Needs SET Deadline = @day WHERE PublicId = @need;", new { need, day = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue) });

        Assert.Empty(await BoardTitlesAsync());
        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 1m), HttpStatusCode.Conflict, "need_closed");
    }

    [Fact]
    public async Task TheListsAreAnonymous_InThreeLanguages()
    {
        var lookups = await BodyOf(await SendAsync(Anonymous(), HttpMethod.Get, "/api/needs/lookups"));

        Assert.Contains(lookups.GetProperty("categories").EnumerateArray(), c => c.GetProperty("code").GetString() == "STATIONERY" && c.GetProperty("nameSi").GetString() is { Length: > 0 });
        Assert.Contains(lookups.GetProperty("units").EnumerateArray(), u => u.GetProperty("code").GetString() == "KG");
        Assert.DoesNotContain(lookups.GetProperty("units").EnumerateArray(), u => u.GetProperty("code").GetString() == "CASH");
    }

    // ------------------------------------------------------------------ pledging (M13)

    [Fact]
    public async Task APledge_IsTheCallers_AndCountsAgainstTheNeed()
    {
        var need = await OpenNeedAsync(50m);
        var donor = await DonorAsync();

        var response = await TryPledgeAsync(donor, need, 12.5m, Today.AddDays(7));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Equal(new Counters(50m, 12.5m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal(donor, await ScalarAsync<Guid>("SELECT DonorUserId FROM giving.Pledges WHERE PublicId = @id;", new { id = (await BodyOf(response)).GetProperty("publicId").GetGuid() }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLogs WHERE EntityName = 'Pledge' AND Action = 'Pledge' AND UserId = @donor;", new { donor }));

        var mine = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, "/api/me/pledges"));
        var row = Assert.Single(mine.EnumerateArray());
        Assert.Equal("Exercise books", row.GetProperty("needTitle").GetString());
        Assert.Equal("Pledged", row.GetProperty("status").GetString());
        Assert.Equal("PIECES", row.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task APledgeBeyondWhatIsLeft_IsRefused_AndChangesNothing()
    {
        var need = await OpenNeedAsync(10m);
        await PledgeAsync(await DonorAsync(), need, 8m);

        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 2.01m), HttpStatusCode.Conflict, "need_full");

        Assert.Equal(new Counters(10m, 8m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Pledges;", new { }));

        await PledgeAsync(await DonorAsync(), need, 2m); // exactly what is left
        Assert.Equal(10m, (await CountersAsync(need)).Pledged);
    }

    [Fact]
    public async Task ThePledgeFormIsChecked()
    {
        var need = await OpenNeedAsync();
        var donor = await DonorAsync();

        await AssertFieldsAsync(await TryPledgeAsync(donor, need, 0m), "quantity");
        await AssertFieldsAsync(await TryPledgeAsync(donor, need, 1.234m), "quantity");
        await AssertFieldsAsync(await TryPledgeAsync(donor, need, 1m, Today.AddDays(-1)), "expectedDate");
        await AssertFieldsAsync(await TryPledgeAsync(donor, need, 1m, Today.AddYears(1).AddDays(1)), "expectedDate");
    }

    [Fact]
    public async Task ANeedThatStopped_RefusesPledges_AndAnUnconfirmedPhoneToo()
    {
        var need = await OpenNeedAsync();
        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(confirmed: false), need, 1m), HttpStatusCode.Forbidden, "phone_required");

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }));
        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 1m), HttpStatusCode.Conflict, "need_closed");
    }

    [Fact]
    public async Task ASuspendedInstitution_TakesNoPledges()
    {
        var need = await OpenNeedAsync();
        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Id = @id;", new { id = database.AlphaInstitutionId });

        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 1m), HttpStatusCode.NotFound, "need_not_found");
    }

    [Fact]
    public async Task ADonorHoldsAtMostFiveOpenPledgesOnANeed()
    {
        var need = await OpenNeedAsync(50m);
        var other = await OpenNeedAsync(50m);
        var donor = await DonorAsync();
        var first = await PledgeAsync(donor, need, 1m);
        for (var i = 0; i < 4; i++)
        {
            await PledgeAsync(donor, need, 1m);
        }

        await AssertRefusedAsync(await TryPledgeAsync(donor, need, 1m), HttpStatusCode.Conflict, "too_many_pledges");
        Assert.Equal(5m, (await CountersAsync(need)).Pledged); // the refused pledge gave its quantity back

        await PledgeAsync(donor, other, 1m); // per need
        await PledgeAsync(await DonorAsync(), need, 1m); // per donor

        await AssertOkAsync(await SendAsync(Personal(donor), HttpMethod.Post, $"/api/me/pledges/{first}/cancel"));
        await PledgeAsync(donor, need, 1m); // open ones only
    }

    /// <summary>
    /// Holds the need's row with an update lock from a separate connection: the callers' reads pass, their
    /// conditional UPDATEs queue behind it, and <c>sys.dm_tran_locks</c> shows when all of them wait.
    /// </summary>
    private async Task<HttpResponseMessage[]> ForcedRaceAsync(Guid need, params Func<Task<HttpResponseMessage>>[] callers)
    {
        await using var holder = await ConnectionAsync(Platform);
        await using var hold = await holder.BeginTransactionAsync();
        // By Id: read by PublicId, the unique index alone answers, and the lock never reaches the clustered row the UPDATEs seek.
        var needId = await holder.ExecuteScalarAsync<int>("SELECT Id FROM giving.Needs WHERE PublicId = @need;", new { need }, hold);
        await holder.ExecuteAsync("SELECT Id FROM giving.Needs WITH (UPDLOCK, ROWLOCK) WHERE Id = @needId;", new { needId }, hold);

        var attempts = callers.Select(c => Task.Run(c)).ToArray();

        await using (var observer = await ConnectionAsync(string.Empty))
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            int waiting;
            do
            {
                waiting = await observer.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM sys.dm_tran_locks l
                    JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                    WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID() AND p.object_id = OBJECT_ID('giving.Needs');
                    """);
                await Task.Delay(25);
            }
            while (waiting < callers.Length && DateTime.UtcNow < deadline);

            Assert.True(waiting >= callers.Length, $"Only {waiting} of {callers.Length} callers queued: the race was not forced.");
        }

        await hold.CommitAsync();
        return await Task.WhenAll(attempts);
    }

    [Fact]
    public async Task TwoDonorsPledgingTheLastUnitsAtOnce_OneWins()
    {
        var need = await OpenNeedAsync(50m);
        await PledgeAsync(await DonorAsync(), need, 40m);
        var first = await DonorAsync();
        var second = await DonorAsync();

        var answers = await ForcedRaceAsync(need, () => TryPledgeAsync(first, need, 10m), () => TryPledgeAsync(second, need, 10m));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Created);
        await AssertRefusedAsync(answers.Single(a => a.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "need_full");
        Assert.Equal(50m, (await CountersAsync(need)).Pledged);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Pledges WHERE Status = 'Pledged';", new { }));
    }

    [Fact]
    public async Task ADonorRacingThemselves_CannotPassTheCap()
    {
        var need = await OpenNeedAsync(50m);
        var donor = await DonorAsync();
        for (var i = 0; i < 4; i++)
        {
            await PledgeAsync(donor, need, 1m);
        }

        var answers = await ForcedRaceAsync(need, () => TryPledgeAsync(donor, need, 1m), () => TryPledgeAsync(donor, need, 1m));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Created);
        await AssertRefusedAsync(answers.Single(a => a.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "too_many_pledges");
        Assert.Equal(5m, (await CountersAsync(need)).Pledged);
    }

    // ------------------------------------------------------------------ cancelling a pledge (M13, M14)

    [Fact]
    public async Task ADonorCancelsTheirOwn_AndTheQuantityComesBack()
    {
        var need = await OpenNeedAsync(10m);
        var donor = await DonorAsync();
        var pledge = await PledgeAsync(donor, need, 10m);

        await AssertOkAsync(await SendAsync(Personal(donor), HttpMethod.Post, $"/api/me/pledges/{pledge}/cancel"));

        Assert.Equal(0m, (await CountersAsync(need)).Pledged);
        Assert.Equal(donor, await ScalarAsync<Guid>("SELECT CancelledBy FROM giving.Pledges WHERE PublicId = @pledge AND Status = 'Cancelled';", new { pledge }));

        await AssertRefusedAsync(await SendAsync(Personal(donor), HttpMethod.Post, $"/api/me/pledges/{pledge}/cancel"), HttpStatusCode.Conflict, "pledge_not_open");
        Assert.Equal(0m, (await CountersAsync(need)).Pledged); // not given back twice
    }

    [Fact]
    public async Task ADonorCannotCancelAnothersPledge_ThoughThatRunsAsThePlatform()
    {
        var need = await OpenNeedAsync(10m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 10m);

        await AssertRefusedAsync(await SendAsync(Personal(await DonorAsync()), HttpMethod.Post, $"/api/me/pledges/{pledge}/cancel"), HttpStatusCode.NotFound, "pledge_not_found");

        Assert.Equal(10m, (await CountersAsync(need)).Pledged);
    }

    [Fact]
    public async Task ADeliveredPledgeCannotBeCancelled()
    {
        var need = await OpenNeedAsync(10m);
        var donor = await DonorAsync();
        var pledge = await PledgeAsync(donor, need, 5m);
        await AssertOkAsync(await ReceiveAsync(pledge, 5m));

        await AssertRefusedAsync(await SendAsync(Personal(donor), HttpMethod.Post, $"/api/me/pledges/{pledge}/cancel"), HttpStatusCode.Conflict, "pledge_not_open");
        Assert.Equal(new Counters(10m, 5m, 5m, "Open"), await CountersAsync(need));
    }

    [Fact]
    public async Task StaffCancelWithAReason_WhichTheDonorReads()
    {
        var need = await OpenNeedAsync(10m);
        var donor = await DonorAsync();
        var pledge = await PledgeAsync(donor, need, 4m);
        var version = await PledgeVersionAsync(pledge);

        await AssertFieldsAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/cancel", new { reason = " ", version }), "reason");
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/cancel", new { reason = "No.", version }), HttpStatusCode.NotFound, "pledge_not_found");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"{Pledges}/{pledge}/cancel", new { reason = "No.", version })).StatusCode);

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/cancel", new { reason = "We no longer need these.", version }));

        Assert.Equal(0m, (await CountersAsync(need)).Pledged);
        var row = Assert.Single((await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, "/api/me/pledges"))).EnumerateArray());
        Assert.Equal("Cancelled", row.GetProperty("status").GetString());
        Assert.Equal("We no longer need these.", row.GetProperty("cancelReason").GetString());
    }

    [Fact]
    public async Task StaffCancellingOnAnOldReading_IsStale_AndGivesNothingBack()
    {
        var need = await OpenNeedAsync(10m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 4m);
        var version = await PledgeVersionAsync(pledge);
        await ExecuteAsync("UPDATE giving.Pledges SET ExpectedDeliveryDate = @day WHERE PublicId = @pledge;", new { pledge, day = Today.AddDays(2).ToDateTime(TimeOnly.MinValue) });

        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/cancel", new { reason = "Changed.", version }), HttpStatusCode.Conflict, "stale");

        Assert.Equal(4m, (await CountersAsync(need)).Pledged);
    }

    // ------------------------------------------------------------------ RLS (M14)

    [Fact]
    public async Task ADonorsSession_CannotWriteANeed()
    {
        var need = await OpenNeedAsync();
        var asDonor = $"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{await DonorAsync()}', @read_only = 1;";

        await using var connection = await ConnectionAsync(asDonor);
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("UPDATE giving.Needs SET Title = N'Hacked' WHERE PublicId = @need;", new { need }));
        Assert.Contains("block predicate", error.Message, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("UPDATE giving.Needs SET QuantityPledged = 0 WHERE PublicId = @need;", new { need }));
    }

    [Fact]
    public async Task AnotherInstitutionsSession_CannotWriteANeed()
    {
        var need = await OpenNeedAsync();
        var asBeta = $"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.BetaInstitutionId}, @read_only = 1;";

        await using var connection = await ConnectionAsync(asBeta);
        await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("UPDATE giving.Needs SET Title = N'Hacked' WHERE PublicId = @need;", new { need }));
    }

    [Fact]
    public async Task APledgeIsReadByItsInstitutionAndItsDonor_AndNobodyElse()
    {
        var need = await OpenNeedAsync();
        var donor = await DonorAsync();
        await PledgeAsync(donor, need, 1m);

        async Task<int> SeenBy(string stamp)
        {
            await using var connection = await ConnectionAsync(stamp);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM giving.Pledges;");
        }

        Assert.Equal(1, await SeenBy($"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.AlphaInstitutionId}, @read_only = 1;"));
        Assert.Equal(1, await SeenBy($"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{donor}', @read_only = 1;"));
        Assert.Equal(0, await SeenBy($"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.BetaInstitutionId}, @read_only = 1;"));
        Assert.Equal(0, await SeenBy($"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{await DonorAsync()}', @read_only = 1;"));
        Assert.Equal(0, await SeenBy(string.Empty));

        Assert.Empty((await BodyOf(await SendAsync(Personal(await DonorAsync()), HttpMethod.Get, "/api/me/pledges"))).EnumerateArray());
    }

    [Fact]
    public async Task AnonymousCallers_ReachOnlyTheBoard()
    {
        var need = await OpenNeedAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Post, $"/api/needs/{need}/pledges", new { quantity = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Get, "/api/me/pledges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Get, Needs)).StatusCode);
    }

    // ------------------------------------------------------------------ receiving (M15)

    [Fact]
    public async Task ADelivery_MakesAnInKindReceipt_ForTheDonor_AndMovesTheCounters()
    {
        var need = await OpenNeedAsync(50m);
        var donor = await DonorAsync("Kamala Pledger");
        var pledge = await PledgeAsync(donor, need, 20m);

        var response = await ReceiveAsync(pledge, 15m);
        await AssertOkAsync(response);
        var body = await BodyOf(response);
        Assert.Equal($"RCP-{Today.Year}-000001", body.GetProperty("receiptNo").GetString());

        // 15 came; the 5 that did not are free again.
        Assert.Equal(new Counters(50m, 15m, 15m, "Open"), await CountersAsync(need));

        var gift = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, $"/api/me/donations/{body.GetProperty("receiptPublicId").GetGuid()}"));
        Assert.Equal("InKind", gift.GetProperty("kind").GetString());
        Assert.Equal(15m, gift.GetProperty("quantity").GetDecimal());
        Assert.Equal("Exercise books", gift.GetProperty("items").GetString());
        Assert.Equal("Kamala Pledger", gift.GetProperty("donorName").GetString());

        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM giving.Pledges p
            JOIN giving.Donations d ON d.PledgeId = p.Id AND d.NeedId = p.NeedId
            WHERE p.PublicId = @pledge AND p.Status = 'Delivered' AND p.ReceivedQuantity = 15 AND p.ReceivedBy = @owner;
            """,
            new { pledge, owner = _alphaOwner }));

        var row = Assert.Single((await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"{Needs}/{need}/pledges"))).EnumerateArray());
        Assert.Equal("Delivered", row.GetProperty("status").GetString());
        Assert.Equal(body.GetProperty("receiptNo").GetString(), row.GetProperty("receiptNo").GetString());
        Assert.Equal("Kamala Pledger", row.GetProperty("donorName").GetString());
    }

    [Fact]
    public async Task TheLastDelivery_FulfilsTheNeed_AndItLeavesTheBoard()
    {
        var need = await OpenNeedAsync(10m);
        var first = await PledgeAsync(await DonorAsync(), need, 4m);
        var second = await PledgeAsync(await DonorAsync(), need, 6m);

        await AssertOkAsync(await ReceiveAsync(first, 4m));
        Assert.Equal("Open", (await CountersAsync(need)).Status);

        await AssertOkAsync(await ReceiveAsync(second, 6m));
        Assert.Equal(new Counters(10m, 10m, 10m, "Fulfilled"), await CountersAsync(need));
        Assert.Empty(await BoardTitlesAsync());
    }

    [Fact]
    public async Task ADeliveryIsChecked_AndTheReceiptNumberIsGivenBack_WhenItIsStale()
    {
        var need = await OpenNeedAsync(10m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 5m);
        var version = await PledgeVersionAsync(pledge);

        await AssertFieldsAsync(await ReceiveAsync(pledge, 5.01m), "quantity");
        await AssertFieldsAsync(await ReceiveAsync(pledge, 0m), "quantity");
        await AssertRefusedAsync(await ReceiveAsync(pledge, 1m, Beta(_betaOwner)), HttpStatusCode.NotFound, "pledge_not_found");
        Assert.Equal(HttpStatusCode.Forbidden, (await ReceiveAsync(pledge, 1m, Alpha(_alphaStaff))).StatusCode);

        await ExecuteAsync("UPDATE giving.Pledges SET ExpectedDeliveryDate = @day WHERE PublicId = @pledge;", new { pledge, day = Today.AddDays(2).ToDateTime(TimeOnly.MinValue) });
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Pledges}/{pledge}/receive", new { quantity = 5m, version }),
            HttpStatusCode.Conflict,
            "stale");

        Assert.Equal(new Counters(10m, 5m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations;", new { }));

        var done = await BodyOf(await ReceiveAsync(pledge, 5m));
        Assert.Equal($"RCP-{Today.Year}-000001", done.GetProperty("receiptNo").GetString());

        await AssertRefusedAsync(await ReceiveAsync(pledge, 5m), HttpStatusCode.Conflict, "pledge_not_open");
        Assert.Equal(new Counters(10m, 5m, 5m, "Open"), await CountersAsync(need));
    }

    [Fact]
    public async Task ADeliveryTextsItsReceipt_ToTheDonorsAccount()
    {
        var need = await OpenNeedAsync(10m);
        var donor = await DonorAsync();
        var pledge = await PledgeAsync(donor, need, 5m);
        await AssertOkAsync(await ReceiveAsync(pledge, 5m));

        var connections = new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform());
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);
        var processor = new OutboxProcessor(
            connections,
            [new DonationSmsHandler(new GivingQueries(connections), texts)],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
        await processor.RunAsync();

        var phone = await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @donor;", new { donor });
        var body = await ScalarAsync<string>("SELECT Body FROM notify.Notifications WHERE Recipient = @phone;", new { phone });
        Assert.Contains($"RCP-{Today.Year}-000001", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exercise books", body, StringComparison.Ordinal); // staff-typed words are never texted (M24)
    }

    // ------------------------------------------------------------------ gifts without a pledge (M15, M29)

    [Fact]
    public async Task ItemsThatCameWithoutAPledge_TakeUpRoom_AndCanFulfilTheNeed()
    {
        var need = await OpenNeedAsync(10m);
        await RecordAsync(ItemsFor(need, 4m));
        Assert.Equal(new Counters(10m, 4m, 4m, "Open"), await CountersAsync(need));

        // The room is gone for a donor too: 7 no longer fit.
        await AssertRefusedAsync(await TryPledgeAsync(await DonorAsync(), need, 7m), HttpStatusCode.Conflict, "need_full");

        await RecordAsync(ItemsFor(need, 6m));
        Assert.Equal(new Counters(10m, 10m, 10m, "Fulfilled"), await CountersAsync(need));
    }

    [Fact]
    public async Task ItemsBeyondTheRoom_AreRefused_AndGiveTheirReceiptNumberBack()
    {
        var need = await OpenNeedAsync(10m);
        await PledgeAsync(await DonorAsync(), need, 8m);

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/donations", ItemsFor(need, 3m)),
            HttpStatusCode.Conflict,
            "need_full");

        Assert.Equal(new Counters(10m, 8m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal($"RCP-{Today.Year}-000001", (await RecordAsync(ItemsFor(need, 2m))).ReceiptNo);
    }

    [Fact]
    public async Task AGiftAgainstANeed_MustSayHowMany_AndBeOfAnOpenNeedOfTheirOwn()
    {
        var need = await OpenNeedAsync(10m);

        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/donations", ItemsFor(need, null)), "quantity");
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Post, "/api/institution/donations", ItemsFor(need, 1m)), HttpStatusCode.NotFound, "need_not_found");

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }));
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/donations", ItemsFor(need, 1m)), HttpStatusCode.Conflict, "need_closed");
    }

    [Fact]
    public async Task MoneyNamesANeed_ButNeverCountsTowardIt()
    {
        var need = await OpenNeedAsync(10m);
        var money = new Dictionary<string, object?>
        {
            ["kind"] = "Money",
            ["amount"] = 2500m,
            ["method"] = "CASH",
            ["date"] = Iso(Today),
            ["donorName"] = "Walk-in Donor",
            ["need"] = need,
        };

        var recorded = await RecordAsync(money);

        Assert.Equal(new Counters(10m, 0m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations d JOIN giving.Needs n ON n.Id = d.NeedId WHERE d.PublicId = @id AND n.PublicId = @need;", new { id = recorded.PublicId, need }));

        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }));
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/donations", money), HttpStatusCode.Conflict, "need_closed");
    }

    // ------------------------------------------------------------------ voiding (M30)

    [Fact]
    public async Task VoidingAGift_TakesItOutOfTheNeed_AndReopensOneItHadFulfilled()
    {
        var need = await OpenNeedAsync(10m);
        var gift = await RecordAsync(ItemsFor(need, 10m));
        Assert.Equal("Fulfilled", (await CountersAsync(need)).Status);

        var response = await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"/api/institution/donations/{gift.ReceiptNo}/void", new { reason = "Counted the wrong box.", version = gift.Version });
        await AssertOkAsync(response);

        Assert.Equal(new Counters(10m, 0m, 0m, "Open"), await CountersAsync(need));
        await PledgeAsync(await DonorAsync(), need, 10m); // the room is back
    }

    [Fact]
    public async Task VoidingADeliveredPledgesReceipt_KeepsThePledgeDelivered_AndCorrectsTheCount()
    {
        var need = await OpenNeedAsync(10m);
        var pledge = await PledgeAsync(await DonorAsync(), need, 6m);
        var receipt = await BodyOf(await ReceiveAsync(pledge, 6m));
        var version = Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Donations WHERE PublicId = @id;", new { id = receipt.GetProperty("receiptPublicId").GetGuid() }));

        await AssertOkAsync(await SendAsync(
            await SteppedUpAlphaAsync(_alphaManager),
            HttpMethod.Post,
            $"/api/institution/donations/{receipt.GetProperty("receiptNo").GetString()}/void",
            new { reason = "Never arrived.", version }));

        Assert.Equal(new Counters(10m, 0m, 0m, "Open"), await CountersAsync(need));
        Assert.Equal("Delivered", await ScalarAsync<string>("SELECT Status FROM giving.Pledges WHERE PublicId = @pledge;", new { pledge }));
    }

    [Fact]
    public async Task AVoidOnAClosedNeed_LeavesItClosed_WithTheTrueCount()
    {
        var need = await OpenNeedAsync(10m);
        var gift = await RecordAsync(ItemsFor(need, 4m));
        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await VersionAsync(need) }));

        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"/api/institution/donations/{gift.ReceiptNo}/void", new { reason = "Mistake.", version = gift.Version }));

        Assert.Equal(new Counters(10m, 0m, 0m, "Closed"), await CountersAsync(need));
    }

    [Fact]
    public async Task AStaleVoid_LeavesTheNeedAlone()
    {
        var need = await OpenNeedAsync(10m);
        var gift = await RecordAsync(ItemsFor(need, 4m));

        await AssertRefusedAsync(
            await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"/api/institution/donations/{gift.ReceiptNo}/void", new { reason = "Mistake.", version = "AAAAAAAAAAE=" }),
            HttpStatusCode.Conflict,
            "stale");

        Assert.Equal(new Counters(10m, 4m, 4m, "Open"), await CountersAsync(need));
    }
}

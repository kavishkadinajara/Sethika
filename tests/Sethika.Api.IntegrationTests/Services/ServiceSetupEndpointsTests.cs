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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Services.Setup;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Services;

/// <summary>
/// The institution's set-up for religious services over HTTP (Phase E, 8.4), against real SQL Server
/// and Redis: service types, the leader roster, their usual week and leave, and the assign sheet's
/// picker. Who may do what, that one institution never reaches another's, and that leave can never
/// take time a leader has already promised.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them, and both
/// have SERVICES on, as approval turns it on for a temple (V12). Appointments are written straight
/// into the tables: the donor's request is 8.5.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ServiceSetupEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "service-setup-test-signing-key-at-least-32-bytes";

    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    private Guid _alphaOwner;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    /// <summary>Ten days ahead in Colombo: inside every type's advance window here.</summary>
    private static DateOnly Day => SriLankaTime.Today(DateTime.UtcNow).AddDays(10);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT i.Id, m.Id, 1, 'Manual', @changedBy
            FROM auth.Modules m CROSS JOIN org.Institutions i
            WHERE m.Code = 'SERVICES' AND i.Id IN (@alpha, @beta);

            -- The template roles, cloned into each institution as onboarding's approval clones them.
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
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _alphaOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");
        _alphaStaff = await MemberAsync(database.AlphaInstitutionId, "STAFF");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");

        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public async Task DisposeAsync()
    {
        // The reseed clears the services, leaders, leave and roles these users made or hold.
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });
        await _host.DisposeAsync();
        await _redis.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private async Task<Guid> UserAsync(string userType, string? phone = null)
    {
        var id = Guid.CreateVersion7();
        phone ??= RandomPhone();

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, N'Services Test', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, userType, phone });

        _users.Add(id);
        return id;
    }

    private static string RandomPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>A member holding the institution's copy of a template role.</summary>
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

    private HttpClient ClientFor(Guid userId, int institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
    }

    private HttpClient Alpha(Guid member) => ClientFor(member, database.AlphaInstitutionId);

    private HttpClient Beta(Guid member) => ClientFor(member, database.BetaInstitutionId);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            return await client.SendAsync(request);
        }
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await BodyOf(response)).GetProperty("code").GetString());
    }

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(fields, body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    /// <summary>The Colombo wall-clock time on <paramref name="day"/>, as the screens send it.</summary>
    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), Colombo);

    private static object TypeBody(string name, string? version = null, string location = "Either") => new
    {
        name,
        nameSi = name + " si",
        description = "Chanted by three monks",
        durationMinutes = 180,
        bufferMinutes = 30,
        leadersRequired = 3,
        locationMode = location,
        minAdvanceDays = 2,
        maxAdvanceDays = 120,
        version,
    };

    /// <summary>A service type made by Alpha's owner through the API; its public id and version.</summary>
    private async Task<(Guid PublicId, string Version)> TypeAsync(string name, string location = "Either")
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/service-types", TypeBody(name, location: location));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        return (body.GetProperty("publicId").GetGuid(), body.GetProperty("version").GetString()!);
    }

    /// <summary>A leader made by Alpha's owner through the API; their public id, internal id and version.</summary>
    private async Task<(Guid PublicId, int Id, string Version)> LeaderAsync(string name, object? extra = null)
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", extra ?? new { displayName = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        var publicId = body.GetProperty("publicId").GetGuid();
        var id = await ScalarAsync<int>("SELECT Id FROM org.ReligiousLeaders WHERE PublicId = @publicId;", new { publicId });
        return (publicId, id, body.GetProperty("version").GetString()!);
    }

    /// <summary>A requested appointment of the type from <paramref name="startsAt"/>; its id and number.</summary>
    private async Task<(long Id, string No)> AppointmentAsync(Guid typePublicId, DateTimeOffset startsAt, string location = ServiceLocation.AtInstitution)
    {
        var donor = await UserAsync("Donor");
        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        var id = await ScalarAsync<long>(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, Address, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Services Test Donor', '+94770000000',
                   @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt), @location,
                   CASE WHEN @location = 'AtDonorPlace' THEN N'12 Temple Road' END, @donor
            FROM booking.ServiceTypes WHERE PublicId = @typePublicId;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { no, donor, startsAt = startsAt.UtcDateTime, location, typePublicId });
        return (id, no);
    }

    /// <summary>What an assignment writes, written directly: the row and its blocks.</summary>
    private async Task AssignAsync(long appointmentId, int leaderId, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT booking.AppointmentLeaders (AppointmentId, LeaderId, InstitutionId, AssignedBy)
            VALUES (@appointmentId, @leaderId, @institutionId, @assignedBy);
            """,
            new { appointmentId, leaderId, institutionId = database.AlphaInstitutionId, assignedBy = _alphaOwner });

        await connection.ExecuteAsync(
            """
            INSERT booking.LeaderTimeBlocks (LeaderId, BlockStart, InstitutionId, Kind, AppointmentId)
            VALUES (@leaderId, @blockStart, @institutionId, 'Appointment', @appointmentId);
            """,
            TimeBlocks.For(startsAt.UtcDateTime, endsAt.UtcDateTime)
                .Select(blockStart => new { leaderId, blockStart, institutionId = database.AlphaInstitutionId, appointmentId }));
    }

    private async Task<int> BlocksOfAsync(int leaderId, string? kind = null) =>
        await ScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.LeaderTimeBlocks WHERE LeaderId = @leaderId AND (@kind IS NULL OR Kind = @kind);",
            new { leaderId, kind });

    private Task<HttpResponseMessage> TimeOffAsync(Guid leader, DateTimeOffset startsAt, DateTimeOffset endsAt, string? reason = "Retreat") =>
        SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/leaders/{leader}/time-off", new { startsAt, endsAt, reason });

    private static async Task<JsonElement> ListAsync(HttpClient client, string path)
    {
        var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyOf(response);
    }

    private static JsonElement Find(JsonElement list, Guid publicId) =>
        list.EnumerateArray().Single(e => e.GetProperty("publicId").GetGuid() == publicId);

    // ------------------------------------------------------------------ service types

    [Fact]
    public async Task AnOwnerAddsEditsAndRetiresAServiceType()
    {
        var (publicId, version) = await TypeAsync("Pirith");

        var listed = Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/service-types"), publicId);
        Assert.Equal("Pirith si", listed.GetProperty("nameSi").GetString());
        Assert.Equal(180, listed.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(30, listed.GetProperty("bufferMinutes").GetInt32());
        Assert.Equal(3, listed.GetProperty("leadersRequired").GetInt32());
        Assert.Equal("Either", listed.GetProperty("locationMode").GetString());
        Assert.True(listed.GetProperty("isActive").GetBoolean());

        var edited = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/service-types/{publicId}", TypeBody("Night pirith", version));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.NotEqual(version, (await BodyOf(edited)).GetProperty("version").GetString());

        // The version read before the edit is stale now.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/service-types/{publicId}", TypeBody("Pirith again", version)),
            HttpStatusCode.Conflict,
            "stale");

        var retired = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/service-types/{publicId}/deactivate");
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);

        listed = Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/service-types"), publicId);
        Assert.Equal("Night pirith", listed.GetProperty("name").GetString());
        Assert.False(listed.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task AServiceFormNamesEveryFieldToFix()
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/service-types", new
        {
            name = " ",
            durationMinutes = 20,
            bufferMinutes = 200,
            leadersRequired = 0,
            locationMode = "Home",
            minAdvanceDays = 5,
            maxAdvanceDays = 2,
        });

        await AssertFieldsAsync(response, "name", "durationMinutes", "bufferMinutes", "leadersRequired", "locationMode", "maxAdvanceDays");
        Assert.Equal(0, (await ListAsync(Alpha(_alphaOwner), "/api/institution/service-types")).GetArrayLength());
    }

    [Fact]
    public async Task TwoServicesCannotShareAName()
    {
        await TypeAsync("Bana");

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/service-types", TypeBody("Bana")),
            HttpStatusCode.Conflict,
            "service_name_taken");
    }

    [Fact]
    public async Task StaffSeeServicesAndLeadersButCannotChangeThem()
    {
        var (type, _) = await TypeAsync("Bodhi pooja");
        var leader = await LeaderAsync("Ven. Staff Test");

        Assert.Equal(1, (await ListAsync(Alpha(_alphaStaff), "/api/institution/service-types")).GetArrayLength());
        Assert.Equal(1, (await ListAsync(Alpha(_alphaStaff), "/api/institution/leaders")).GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/service-types", TypeBody("Staff's"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"/api/institution/service-types/{type}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Ven. X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TimeOffAsStaff(leader.PublicId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}/weekly-hours", new { spans = Array.Empty<object>() })).StatusCode);

        Task<HttpResponseMessage> TimeOffAsStaff(Guid publicId) =>
            SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"/api/institution/leaders/{publicId}/time-off", new { startsAt = At(Day, 8), endsAt = At(Day, 9) });
    }

    [Fact]
    public async Task AnotherInstitutionsServicesAndLeadersAreNotFound()
    {
        var (type, version) = await TypeAsync("Alpha's pirith");
        var leader = await LeaderAsync("Ven. Alpha");

        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Put, $"/api/institution/service-types/{type}", TypeBody("Taken", version)),
            HttpStatusCode.NotFound,
            "service_type_not_found");
        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Post, $"/api/institution/leaders/{leader.PublicId}/deactivate"),
            HttpStatusCode.NotFound,
            "leader_not_found");
        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Post, $"/api/institution/leaders/{leader.PublicId}/time-off", new { startsAt = At(Day, 8), endsAt = At(Day, 9) }),
            HttpStatusCode.NotFound,
            "leader_not_found");

        // Service types are public to read under RLS; the list still names the institution.
        Assert.Equal(0, (await ListAsync(Beta(_betaOwner), "/api/institution/service-types")).GetArrayLength());
        Assert.Equal(0, (await ListAsync(Beta(_betaOwner), "/api/institution/leaders")).GetArrayLength());
        Assert.Equal(0, await BlocksOfAsync(leader.Id));
    }

    // ------------------------------------------------------------------ leaders

    [Fact]
    public async Task AnOwnerAddsALeaderWithATextablePhone()
    {
        var leader = await LeaderAsync("Ven. Ananda", new { displayName = "Ven. Ananda", title = "Ven.", phone = "077 123 4567" });

        var listed = Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId);
        Assert.Equal("Ven. Ananda", listed.GetProperty("displayName").GetString());
        Assert.Equal("Ven.", listed.GetProperty("title").GetString());
        Assert.Equal("+94771234567", listed.GetProperty("phone").GetString());
        Assert.Equal("si", listed.GetProperty("language").GetString());
        Assert.False(listed.GetProperty("hasAccount").GetBoolean());
        Assert.True(listed.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, listed.GetProperty("weeklyHours").GetArrayLength());
    }

    [Fact]
    public async Task ALeaderFormNamesEveryFieldToFix()
    {
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "", phone = "12345", language = "fr" }),
            "displayName",
            "phone",
            "language");

        // Linking finds the account by its phone, so there must be one.
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Rev. Silva", linkAccount = true }),
            "phone");

        // A leader is texted their assignments; a landline would never get one (QA DEF-002).
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Rev. Silva", phone = "0112345678" }),
            "phone");

        Assert.Equal(0, (await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders")).GetArrayLength());
    }

    [Fact]
    public async Task ALeaderWhoSignsInIsLinkedByPhone()
    {
        var phone = RandomPhone();
        var account = await UserAsync("InstitutionStaff", phone);
        var local = "0" + phone[3..];

        var leader = await LeaderAsync("Fr. Perera", new { displayName = "Fr. Perera", phone = local, linkAccount = true, language = "en" });
        Assert.Equal(account, await ScalarAsync<Guid>("SELECT UserId FROM org.ReligiousLeaders WHERE Id = @id;", new { id = leader.Id }));
        Assert.True(Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId).GetProperty("hasAccount").GetBoolean());

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Fr. Perera twice", phone, linkAccount = true }),
            HttpStatusCode.Conflict,
            "account_already_linked");

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Fr. Nobody", phone = RandomPhone(), linkAccount = true }),
            HttpStatusCode.Conflict,
            "account_not_found");

        var unlinked = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Fr. Perera", phone, linkAccount = false, version = leader.Version });
        Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        Assert.Null(await ScalarAsync<Guid?>("SELECT UserId FROM org.ReligiousLeaders WHERE Id = @id;", new { id = leader.Id }));

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Fr. Stale", version = leader.Version }),
            HttpStatusCode.Conflict,
            "stale");
    }

    /// <summary>Whether <paramref name="account"/> holds Alpha's LEADER role in force, and their permissions version.</summary>
    private async Task<(bool Holds, int Version)> LeaderRoleOfAsync(Guid account) =>
        (await ScalarAsync<int>(
             """
             SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
             WHERE ur.UserId = @account AND ur.IsActive = 1 AND r.Code = 'LEADER' AND r.InstitutionId = @alpha;
             """,
             new { account, alpha = database.AlphaInstitutionId }) == 1,
         await ScalarAsync<int>("SELECT PermissionsVersion FROM auth.Users WHERE Id = @account;", new { account }));

    private async Task<HttpStatusCode> ScheduleStatusAsync(Guid account)
    {
        using var response = await SendAsync(Alpha(account), HttpMethod.Get, "/api/institution/schedule");
        return response.StatusCode;
    }

    [Fact]
    public async Task LinkingALeaderGivesTheirAccountTheirScheduleAndUnlinkingTakesItBack()
    {
        // QA DEF-001: no owner or manager holds SERVICES.MYSCHEDULE.*, so none may hand out LEADER;
        // the link is how a leader who signs in comes to see their schedule.
        var first = await UserAsync("Donor");
        var second = await UserAsync("Donor");
        var firstPhone = await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @first;", new { first });
        var secondPhone = await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @second;", new { second });
        var before = await LeaderRoleOfAsync(first);
        Assert.False(before.Holds);
        Assert.Equal(HttpStatusCode.Forbidden, await ScheduleStatusAsync(first));

        var leader = await LeaderAsync("Ven. Sumana", new { displayName = "Ven. Sumana", phone = firstPhone, linkAccount = true });
        var linked = await LeaderRoleOfAsync(first);
        Assert.True(linked.Holds);
        Assert.True(linked.Version > before.Version, "the permission cache must reload");
        Assert.Equal(HttpStatusCode.OK, await ScheduleStatusAsync(first));
        Assert.Equal(_alphaOwner, await ScalarAsync<Guid>(
            "SELECT ur.AssignedBy FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @first AND ur.IsActive = 1 AND r.Code = 'LEADER';",
            new { first }));

        // Off the roster they keep it: they may still hold appointments to answer for.
        string version;
        using (var off = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/leaders/{leader.PublicId}/deactivate"))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            version = (await BodyOf(off)).GetProperty("version").GetString()!;
        }

        Assert.True((await LeaderRoleOfAsync(first)).Holds);

        // An edit that keeps the same account touches neither the role nor the permission cache.
        var kept = await LeaderRoleOfAsync(first);
        using (var renamed = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Ven. Sumana Thero", phone = firstPhone, linkAccount = true, version }))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            version = (await BodyOf(renamed)).GetProperty("version").GetString()!;
        }

        Assert.Equal(kept, await LeaderRoleOfAsync(first));
        Assert.Equal(HttpStatusCode.OK, await ScheduleStatusAsync(first));

        // A stale save changes no role either.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Ven. Sumana", phone = secondPhone, linkAccount = true, version = leader.Version }),
            HttpStatusCode.Conflict,
            "stale");
        Assert.True((await LeaderRoleOfAsync(first)).Holds);
        Assert.False((await LeaderRoleOfAsync(second)).Holds);

        // Linked to someone else: the role moves with the record.
        using (var moved = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Ven. Sumana", phone = secondPhone, linkAccount = true, version }))
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            version = (await BodyOf(moved)).GetProperty("version").GetString()!;
        }

        Assert.False((await LeaderRoleOfAsync(first)).Holds);
        Assert.True((await LeaderRoleOfAsync(second)).Holds);
        Assert.Equal(HttpStatusCode.Forbidden, await ScheduleStatusAsync(first));
        Assert.Equal(HttpStatusCode.OK, await ScheduleStatusAsync(second));

        // Unlinked: gone, and the second account is told by its next request.
        var linkedVersion = (await LeaderRoleOfAsync(second)).Version;
        using (var unlinked = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader.PublicId}", new { displayName = "Ven. Sumana", phone = secondPhone, linkAccount = false, version }))
        {
            Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        }

        var after = await LeaderRoleOfAsync(second);
        Assert.False(after.Holds);
        Assert.True(after.Version > linkedVersion);
        Assert.Equal(HttpStatusCode.Forbidden, await ScheduleStatusAsync(second));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'RolePermissionsChanged' AND Payload LIKE '%LeaderUnlinked%' AND (Payload LIKE '%' + CAST(@first AS VARCHAR(36)) + '%' OR Payload LIKE '%' + CAST(@second AS VARCHAR(36)) + '%');",
            new { first, second }));
    }

    [Fact]
    public async Task AnAccountAlreadyLinkedGetsNoSecondRole()
    {
        var account = await UserAsync("Donor");
        var phone = await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @account;", new { account });

        await LeaderAsync("Ven. Once", new { displayName = "Ven. Once", phone, linkAccount = true });
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/leaders", new { displayName = "Ven. Twice", phone, linkAccount = true }),
            HttpStatusCode.Conflict,
            "account_already_linked");

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @account AND r.Code = 'LEADER';",
            new { account }));
    }

    [Fact]
    public async Task ALeadersWeekIsReplacedWhole()
    {
        var leader = await LeaderAsync("Kurukkal Test");
        var path = $"/api/institution/leaders/{leader.PublicId}/weekly-hours";

        var set = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, path, new
        {
            spans = new[]
            {
                new { dayOfWeek = 3, startTime = "06:00", endTime = "09:00" },
                new { dayOfWeek = 1, startTime = "16:00", endTime = "18:00" },
                new { dayOfWeek = 1, startTime = "08:00", endTime = "12:00" },
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        string[] Week(JsonElement listed) =>
            [.. listed.GetProperty("weeklyHours").EnumerateArray().Select(h =>
                $"{h.GetProperty("dayOfWeek").GetInt32()} {h.GetProperty("startTime").GetString()}-{h.GetProperty("endTime").GetString()}")];

        string[] expected = ["1 08:00-12:00", "1 16:00-18:00", "3 06:00-09:00"];
        Assert.Equal(expected, Week(Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId)));

        // Overlapping spans, a day past Saturday and an end before the start: nothing is replaced.
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, path, new
            {
                spans = new[]
                {
                    new { dayOfWeek = 1, startTime = "08:00", endTime = "12:00" },
                    new { dayOfWeek = 1, startTime = "11:00", endTime = "13:00" },
                    new { dayOfWeek = 7, startTime = "08:00", endTime = "09:00" },
                    new { dayOfWeek = 2, startTime = "10:00", endTime = "09:00" },
                },
            }),
            "weeklyHours[1]",
            "weeklyHours[2]",
            "weeklyHours[3]");
        Assert.Equal(expected, Week(Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId)));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, path, new { spans = Array.Empty<object>() })).StatusCode);
        Assert.Empty(Week(Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId)));
    }

    [Fact]
    public async Task TakingALeaderOffTheRosterNamesWhatTheyStillHold()
    {
        var (type, _) = await TypeAsync("Funeral rites");
        var leader = await LeaderAsync("Ven. Held");
        var appointment = await AppointmentAsync(type, At(Day, 10));
        await AssignAsync(appointment.Id, leader.Id, At(Day, 10), At(Day, 13));

        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/leaders/{leader.PublicId}/deactivate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([appointment.No], Strings((await BodyOf(response)).GetProperty("appointments")));
        Assert.False(Find(await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders"), leader.PublicId).GetProperty("isActive").GetBoolean());

        // Not undone: the assignment and its time stay until the institution reassigns.
        Assert.Equal(12, await BlocksOfAsync(leader.Id, LeaderTimeBlockKind.Appointment));
    }

    // ------------------------------------------------------------------ leave

    [Fact]
    public async Task LeaveTakesTheLeadersTimeAndRemovingItGivesItBack()
    {
        var leader = await LeaderAsync("Ven. On Leave");

        var added = await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 10));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await BodyOf(added)).GetProperty("id").GetInt32();

        Assert.Equal(8, await BlocksOfAsync(leader.Id, LeaderTimeBlockKind.TimeOff));
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.LeaderTimeBlocks WHERE TimeOffId = @id;", new { id }));

        var listed = (await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders/time-off")).EnumerateArray().Single();
        Assert.Equal(id, listed.GetProperty("id").GetInt32());
        Assert.Equal(leader.PublicId, listed.GetProperty("leader").GetGuid());
        Assert.Equal(At(Day, 8).UtcDateTime, listed.GetProperty("startsAt").GetDateTime().ToUniversalTime());
        Assert.Equal("Retreat", listed.GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/leaders/time-off/{id}")).StatusCode);
        Assert.Equal(0, await BlocksOfAsync(leader.Id));
        Assert.Equal(0, (await ListAsync(Alpha(_alphaOwner), "/api/institution/leaders/time-off")).GetArrayLength());

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/leaders/time-off/{id}"),
            HttpStatusCode.NotFound,
            "time_off_not_found");
    }

    [Fact]
    public async Task LeaveOverAnAppointmentIsRefusedAndNamesIt()
    {
        var (type, _) = await TypeAsync("House blessing");
        var leader = await LeaderAsync("Ven. Promised");
        var appointment = await AppointmentAsync(type, At(Day, 10));
        await AssignAsync(appointment.Id, leader.Id, At(Day, 10), At(Day, 11));

        var response = await TimeOffAsync(leader.PublicId, At(Day, 9), At(Day, 12));

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "time_off_clashes");
        Assert.Equal([appointment.No], Strings((await BodyOf(response)).GetProperty("appointments")));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM org.LeaderTimeOff WHERE LeaderId = @id;", new { id = leader.Id }));
        Assert.Equal(4, await BlocksOfAsync(leader.Id));

        // Leave that only touches it, ending as it begins, takes nothing it holds.
        Assert.Equal(HttpStatusCode.Created, (await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 10))).StatusCode);
    }

    [Fact]
    public async Task LeaveOverLeaveIsRefused()
    {
        var leader = await LeaderAsync("Ven. Twice");
        Assert.Equal(HttpStatusCode.Created, (await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 12))).StatusCode);

        await AssertRefusedAsync(await TimeOffAsync(leader.PublicId, At(Day, 11, 45), At(Day, 14)), HttpStatusCode.Conflict, "time_off_overlaps");
        Assert.Equal(16, await BlocksOfAsync(leader.Id));
    }

    [Fact]
    public async Task LeaveMustBeOnTheGridNotOverAndAtMostAMonth()
    {
        var leader = await LeaderAsync("Ven. Form");
        var today = SriLankaTime.Today(DateTime.UtcNow);

        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(Day, 8, 10), At(Day, 9)), "startsAt");
        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 9, 5)), "endsAt");
        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(today.AddDays(-3), 8), At(today.AddDays(-2), 8)), "endsAt");
        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 8)), "endsAt");
        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day.AddDays(31), 8, 15)), "endsAt");
        await AssertFieldsAsync(await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 9), new string('x', 201)), "reason");

        // Exactly a month is allowed: 31 days of 96 blocks.
        Assert.Equal(HttpStatusCode.Created, (await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day.AddDays(31), 8))).StatusCode);
        Assert.Equal(31 * 96, await BlocksOfAsync(leader.Id));
    }

    [Fact]
    public async Task LeaveAlreadyBegunTakesTimeOnlyFromNowOn()
    {
        var leader = await LeaderAsync("Ven. Begun");
        var step = TimeSpan.FromMinutes(TimeBlocks.Minutes).Ticks;
        var now = DateTime.UtcNow;
        var current = new DateTime(now.Ticks - (now.Ticks % step), DateTimeKind.Utc);

        var response = await TimeOffAsync(leader.PublicId, new DateTimeOffset(current.AddHours(-2)), new DateTimeOffset(current.AddHours(1)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var connection = await PlatformConnectionAsync();
        var (first, last) = await connection.QuerySingleAsync<(DateTime, DateTime)>(
            "SELECT MIN(BlockStart), MAX(BlockStart) FROM booking.LeaderTimeBlocks WHERE LeaderId = @id;",
            new { id = leader.Id });

        // The block now under way (or the next, if the request crossed into it), never one before.
        Assert.InRange(first, current, current.AddMinutes(TimeBlocks.Minutes));
        Assert.Equal(current.AddMinutes(45), last);

        // The leave itself is kept as asked for.
        Assert.Equal(current.AddHours(-2), await connection.ExecuteScalarAsync<DateTime>("SELECT StartsAt FROM org.LeaderTimeOff WHERE LeaderId = @id;", new { id = leader.Id }));
    }

    [Fact]
    public async Task LeaveWaitsForTheLeadersLock()
    {
        var leader = await LeaderAsync("Ven. Locked");
        var key = "lock:leader:" + leader.Id.ToString(CultureInfo.InvariantCulture);
        await _redis.GetDatabase().StringSetAsync(key, "someone-assigning", TimeSpan.FromSeconds(30));

        try
        {
            await AssertRefusedAsync(await TimeOffAsync(leader.PublicId, At(Day, 8), At(Day, 9)), HttpStatusCode.Conflict, "try_again");
            Assert.Equal(0, await BlocksOfAsync(leader.Id));
        }
        finally
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }
    }

    /// <summary>
    /// Without the lock (Redis down), two overlapping leaves both pass the clash read; the key keeps
    /// only one. Forced: both handlers queue on a table lock on <c>org.LeaderTimeOff</c>, which they
    /// reach only after that read.
    /// </summary>
    [Fact]
    public async Task TwoLeavesAtOnceWithoutTheLockKeepOnlyOne()
    {
        var leader = await LeaderAsync("Ven. Raced");

        await using var holder = await PlatformConnectionAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT TOP (0) Id FROM org.LeaderTimeOff WITH (TABLOCKX, HOLDLOCK);", transaction: hold);

        var running = new[]
        {
            Task.Run(() => AddTimeOffWithoutLockAsync(leader.PublicId, At(Day, 8), At(Day, 10))),
            Task.Run(() => AddTimeOffWithoutLockAsync(leader.PublicId, At(Day, 9), At(Day, 11))),
        };

        await using (var observer = new SqlConnection(database.ConnectionString))
        {
            await observer.OpenAsync();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            int waiting;
            do
            {
                waiting = await observer.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM sys.dm_tran_locks
                    WHERE request_status = 'WAIT' AND resource_database_id = DB_ID()
                      AND resource_type = 'OBJECT' AND resource_associated_entity_id = OBJECT_ID('org.LeaderTimeOff');
                    """);
                if (waiting < 2)
                {
                    await Task.Delay(25);
                }
            }
            while (waiting < 2 && DateTime.UtcNow < deadline);

            Assert.True(waiting >= 2, $"Only {waiting} of 2 callers queued on org.LeaderTimeOff: the race was not forced.");
        }

        await hold.CommitAsync();
        var results = await Task.WhenAll(running);

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == ServiceErrors.TimeOffClashes);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM org.LeaderTimeOff WHERE LeaderId = @id;", new { id = leader.Id }));
        Assert.Equal(8, await BlocksOfAsync(leader.Id));
    }

    private async Task<SetupChangeResult> AddTimeOffWithoutLockAsync(Guid leader, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(
                new TestCurrentUserContext { UserId = _alphaOwner, InstitutionId = database.AlphaInstitutionId }))
            .Options);

        return await new ServiceSetupHandler(context, new NoLocks(), new PhoneNumberNormalizer(), TimeProvider.System)
            .AddTimeOffAsync(database.AlphaInstitutionId, _alphaOwner, leader, new TimeOffInput(startsAt, endsAt, null));
    }

    /// <summary>What the handler sees when Redis is down: every attempt goes on without the lock.</summary>
    private sealed class NoLocks : ILeaderLocks
    {
        public Task<ILockLease> AcquireAsync(IEnumerable<int> leaderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<ILockLease>(new Unlocked());

        private sealed class Unlocked : ILockLease
        {
            public LockOutcome Outcome => LockOutcome.Unavailable;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // ------------------------------------------------------------------ the assign sheet's picker

    [Fact]
    public async Task ThePickerSaysWhoIsAssignedFreeAndNormallyAvailable()
    {
        var (type, _) = await TypeAsync("Sermon");
        var appointment = await AppointmentAsync(type, At(Day, 10));    // 10:00-13:00 Colombo
        var other = await AppointmentAsync(type, At(Day, 12));          // 12:00-15:00
        var day = (int)Day.DayOfWeek;

        var assigned = await LeaderAsync("A assigned");
        var usual = await LeaderAsync("B usual hours");
        var noWeek = await LeaderAsync("C no week");
        var offHours = await LeaderAsync("D outside hours");
        var onLeave = await LeaderAsync("E on leave");
        var elsewhere = await LeaderAsync("F elsewhere");
        var retired = await LeaderAsync("G retired");

        await AssignAsync(appointment.Id, assigned.Id, At(Day, 10), At(Day, 13));
        await AssignAsync(other.Id, elsewhere.Id, At(Day, 12), At(Day, 15));
        Assert.Equal(HttpStatusCode.Created, (await TimeOffAsync(onLeave.PublicId, At(Day, 12, 45), At(Day, 18))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/leaders/{retired.PublicId}/deactivate")).StatusCode);

        await WeekAsync(usual.PublicId, new { dayOfWeek = day, startTime = "06:00", endTime = "13:00" });
        await WeekAsync(offHours.PublicId, new { dayOfWeek = day, startTime = "10:30", endTime = "18:00" }, new { dayOfWeek = (day + 1) % 7, startTime = "06:00", endTime = "20:00" });

        var picked = await ListAsync(Alpha(_alphaOwner), $"/api/institution/appointments/{appointment.No}/candidates");

        string Row(JsonElement c) => string.Join(
            '|',
            c.GetProperty("displayName").GetString(),
            c.GetProperty("assigned").GetBoolean(),
            c.GetProperty("busy").ValueKind == JsonValueKind.Null ? "-" : c.GetProperty("busy").GetString(),
            c.GetProperty("normallyAvailable").ValueKind == JsonValueKind.Null ? "-" : c.GetProperty("normallyAvailable").GetBoolean().ToString());

        Assert.Equal(
            [
                "A assigned|True|-|-",
                "B usual hours|False|-|True",
                "C no week|False|-|-",
                "D outside hours|False|-|False",
                "E on leave|False|TimeOff|-",
                "F elsewhere|False|Appointment|-",
            ],
            picked.EnumerateArray().Select(Row).ToArray());
    }

    [Fact]
    public async Task ThePickerCountsTravelTimeOnlyForAHomeVisit()
    {
        var (type, _) = await TypeAsync("Home pirith");                 // 30 minutes either side
        var home = await AppointmentAsync(type, At(Day, 10), ServiceLocation.AtDonorPlace);
        var temple = await AppointmentAsync(type, At(Day, 10));
        var leader = await LeaderAsync("Travelling");

        // Leave ends at 09:45: inside the home visit's half hour before, clear of the temple's start.
        Assert.Equal(HttpStatusCode.Created, (await TimeOffAsync(leader.PublicId, At(Day, 9), At(Day, 9, 45))).StatusCode);

        var forHome = (await ListAsync(Alpha(_alphaOwner), $"/api/institution/appointments/{home.No}/candidates")).EnumerateArray().Single();
        var forTemple = (await ListAsync(Alpha(_alphaOwner), $"/api/institution/appointments/{temple.No}/candidates")).EnumerateArray().Single();

        Assert.Equal("TimeOff", forHome.GetProperty("busy").GetString());
        Assert.Equal(JsonValueKind.Null, forTemple.GetProperty("busy").ValueKind);
    }

    [Fact]
    public async Task ThePickerIsForAssignersOfTheInstitutionOnly()
    {
        var (type, _) = await TypeAsync("Memorial prayers");
        var appointment = await AppointmentAsync(type, At(Day, 10));
        var path = $"/api/institution/appointments/{appointment.No}/candidates";

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, path)).StatusCode);
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Get, path), HttpStatusCode.NotFound, "appointment_not_found");
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/appointments/APT-NONE/candidates"), HttpStatusCode.NotFound, "appointment_not_found");
    }

    /// <summary>
    /// RLS's owner branch shows a donor their own requests anywhere (V9): Alpha's owner, who asked
    /// Beta for a service, can read that appointment. Acting for Alpha, it is still not found.
    /// </summary>
    [Fact]
    public async Task ThePickerIgnoresTheCallersOwnRequestsAtAnotherInstitution()
    {
        var betaType = await ScalarAsync<Guid>(
            """
            DECLARE @publicId UNIQUEIDENTIFIER = NEWID();
            INSERT booking.ServiceTypes (PublicId, InstitutionId, Name, DurationMinutes, CreatedBy)
            VALUES (@publicId, @beta, N'Beta pirith', 60, @createdBy);
            SELECT @publicId;
            """,
            new { beta = database.BetaInstitutionId, createdBy = _betaOwner });
        await LeaderAsync("Alpha's own");

        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        await ExecuteAsync(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Alpha owner as a donor', '+94770000000',
                   @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt), 'AtInstitution', @donor
            FROM booking.ServiceTypes WHERE PublicId = @betaType;
            """,
            new { no, donor = _alphaOwner, startsAt = At(Day, 10).UtcDateTime, betaType });

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"/api/institution/appointments/{no}/candidates"),
            HttpStatusCode.NotFound,
            "appointment_not_found");
    }

    private async Task WeekAsync(Guid leader, params object[] spans) =>
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/leaders/{leader}/weekly-hours", new { spans })).StatusCode);
}

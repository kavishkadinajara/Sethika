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
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Services;

/// <summary>
/// The institution's side of service appointments over HTTP (Phase E, 8.7), against real SQL Server
/// and Redis: the agenda, assigning and removing leaders, answers by the leader or on their behalf,
/// rejecting, cancelling and completing, and a leader's own schedule. Whoever holds which permission,
/// and that every change frees exactly the time it should.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them, and both
/// have SERVICES on (V12). Appointments, leaders and past assignments are written straight into the
/// tables; the donor's request is tested in <see cref="DonorAppointmentEndpointsTests"/>.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class InstitutionAppointmentEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "institution-appointment-test-signing-key-32-bytes";

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;
    private Guid _alphaStaff;
    private Guid _alphaLeaderUser;
    private Guid _betaOwner;
    private Guid _donor;

    private static DateOnly Day => SriLankaTime.Today(DateTime.UtcNow).AddDays(10);

    private static DateTime At(DateOnly day, int hour, int minute = 0) => SriLankaTime.ToUtc(day, new TimeOnly(hour, minute));

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT i.Id, m.Id, 1, 'Manual', @changedBy
            FROM auth.Modules m CROSS JOIN org.Institutions i
            WHERE m.Code = 'SERVICES' AND i.Id IN (@alpha, @beta);

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
        _alphaLeaderUser = await MemberAsync(database.AlphaInstitutionId, "LEADER");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");
        _donor = await UserAsync("Donor");

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM core.OutboxMessages WHERE AggregateType = 'ServiceAppointment';
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users });
        await _host.DisposeAsync();
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

    private async Task<Guid> UserAsync(string userType)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, N'Appointments Test', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, userType, phone });

        _users.Add(id);
        return id;
    }

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

    private static async Task AssertOkAsync(HttpResponseMessage response, string status)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, (await BodyOf(response)).GetProperty("status").GetString());
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await BodyOf(response)).GetProperty("code").GetString());
    }

    /// <summary>A service type of the institution (Alpha by default); its public id.</summary>
    private async Task<Guid> TypeAsync(byte leadersRequired = 2, int? institutionId = null)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.ServiceTypes (PublicId, InstitutionId, Name, DurationMinutes, BufferMinutes, LeadersRequired, LocationMode, CreatedBy)
            VALUES (@publicId, @institutionId, @name, 120, 30, @leadersRequired, 'Either', @createdBy);
            """,
            new { publicId, institutionId = institutionId ?? database.AlphaInstitutionId, name = "Bana " + Guid.NewGuid().ToString("N")[..8], leadersRequired, createdBy = SethikaTestDatabase.AlphaUserId });
        return publicId;
    }

    /// <summary>A leader on the roster (Alpha's by default), linked to an account if one is named; their public and internal ids.</summary>
    private async Task<(Guid PublicId, int Id)> LeaderAsync(string name, Guid? userId = null, int? institutionId = null)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT org.ReligiousLeaders (PublicId, InstitutionId, UserId, DisplayName, IsActive, CreatedBy)
            VALUES (@publicId, @institutionId, @userId, @name, 1, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { publicId, institutionId = institutionId ?? database.AlphaInstitutionId, userId, name, createdBy = SethikaTestDatabase.AlphaUserId });
        return (publicId, id);
    }

    /// <summary>An appointment of the type, at any time and status, written directly; its id and number.</summary>
    private async Task<(long Id, string No)> AppointmentAsync(Guid type, DateTime startsAt, string status = AppointmentStatus.Requested, Guid? donor = null)
    {
        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        var id = await ScalarAsync<long>(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, Status, CancelledAt, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Appointments Donor', '+94770000000',
                   @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt), 'AtInstitution', @status,
                   CASE WHEN @status = 'Cancelled' THEN SYSUTCDATETIME() END, @donor
            FROM booking.ServiceTypes WHERE PublicId = @type;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { no, donor = donor ?? _donor, type, startsAt, status });
        return (id, no);
    }

    /// <summary>What an assignment writes, written directly (for appointments the API would no longer assign).</summary>
    private async Task AssignDirectAsync(long appointmentId, int leaderId, string response = AssignmentResponse.Pending)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            UPDATE booking.ServiceAppointments SET Status = 'Approved' WHERE Id = @appointmentId AND Status = 'Requested';
            INSERT booking.AppointmentLeaders (AppointmentId, LeaderId, InstitutionId, Response, AssignedBy)
            VALUES (@appointmentId, @leaderId, @institutionId, @response, @by);
            """,
            new { appointmentId, leaderId, institutionId = database.AlphaInstitutionId, response, by = SethikaTestDatabase.AlphaUserId });

        if (AssignmentResponse.IsLive(response))
        {
            var (startsAt, endsAt) = await connection.QuerySingleAsync<(DateTime, DateTime)>(
                "SELECT StartsAt, EndsAt FROM booking.ServiceAppointments WHERE Id = @appointmentId;", new { appointmentId });
            await connection.ExecuteAsync(
                """
                INSERT booking.LeaderTimeBlocks (LeaderId, BlockStart, InstitutionId, Kind, AppointmentId)
                VALUES (@leaderId, @blockStart, @institutionId, 'Appointment', @appointmentId);
                """,
                TimeBlocks.For(startsAt, endsAt).Select(blockStart => new { leaderId, blockStart, institutionId = database.AlphaInstitutionId, appointmentId }));
        }
    }

    private Task<HttpResponseMessage> AssignAsync(string no, params Guid[] leaders) =>
        SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/leaders", new { leaders });

    private Task<int> BlocksAsync(long appointmentId, int? leaderId = null) =>
        ScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.LeaderTimeBlocks WHERE AppointmentId = @appointmentId AND (@leaderId IS NULL OR LeaderId = @leaderId);",
            new { appointmentId, leaderId });

    private async Task<(string Response, bool OnBehalf, Guid? RespondedBy, string? Reason)> AnswerOfAsync(long appointmentId, int leaderId)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<(string, bool, Guid?, string?)>(
            "SELECT Response, RespondedOnBehalf, RespondedBy, DeclineReason FROM booking.AppointmentLeaders WHERE AppointmentId = @appointmentId AND LeaderId = @leaderId;",
            new { appointmentId, leaderId });
    }

    private Task<int> OutboxCountAsync(string no, string eventType) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateId = @no AND EventType = @eventType;", new { no, eventType });

    private async Task<JsonElement> AgendaAsync(string? view = null, Guid? member = null)
    {
        using var response = await SendAsync(Alpha(member ?? _alphaOwner), HttpMethod.Get, "/api/institution/appointments" + (view is null ? string.Empty : "?view=" + view));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyOf(response);
    }

    private static string[] Numbers(JsonElement list) => [.. list.EnumerateArray().Select(a => a.GetProperty("appointmentNo").GetString()!)];

    // ------------------------------------------------------------------ the agenda

    [Fact]
    public async Task TheAgendaShowsWhatNeedsTheInstitution_ByView()
    {
        var pair = await TypeAsync(leadersRequired: 2);
        var single = await TypeAsync(leadersRequired: 1);
        var (a, b) = (await LeaderAsync("Ven. Ananda"), await LeaderAsync("Ven. Bodhi"));

        var (_, waiting) = await AppointmentAsync(pair, At(Day, 8));
        var (shortId, shortNo) = await AppointmentAsync(pair, At(Day, 11));
        await AssignDirectAsync(shortId, a.Id);
        await AssignDirectAsync(shortId, b.Id, AssignmentResponse.Declined);
        var (fullId, full) = await AppointmentAsync(single, At(Day, 15));
        await AssignDirectAsync(fullId, a.Id);
        var (_, cancelled) = await AppointmentAsync(pair, At(Day, 18), AppointmentStatus.Cancelled);
        var (pastId, past) = await AppointmentAsync(pair, At(Day.AddDays(-20), 9));
        await AssignDirectAsync(pastId, b.Id);

        // Beta's, asked for by Alpha's owner as a donor: RLS's owner branch shows it to them (V9).
        await AppointmentAsync(await TypeAsync(institutionId: database.BetaInstitutionId), At(Day, 9), donor: _alphaOwner);

        Assert.Equal(new[] { waiting, shortNo, full }, Numbers(await AgendaAsync()));
        Assert.Equal(new[] { waiting, shortNo, full }, Numbers(await AgendaAsync("upcoming")));
        Assert.Equal(new[] { waiting }, Numbers(await AgendaAsync("waiting")));
        Assert.Equal(new[] { waiting, shortNo }, Numbers(await AgendaAsync("needs-leaders")));
        Assert.Equal(new[] { cancelled, past }, Numbers(await AgendaAsync("history")));

        var upcoming = (await AgendaAsync()).EnumerateArray().ToList();
        var shortOne = upcoming[1];
        Assert.True(shortOne.GetProperty("needsLeaders").GetBoolean());
        Assert.False(upcoming[2].GetProperty("needsLeaders").GetBoolean());
        Assert.Equal(2, shortOne.GetProperty("leadersRequired").GetInt32());
        Assert.Equal("Appointments Donor", shortOne.GetProperty("donorName").GetString());

        // The live leader first, then the one who declined.
        var leaders = shortOne.GetProperty("leaders").EnumerateArray().ToList();
        Assert.Equal(new[] { (a.PublicId, "Pending"), (b.PublicId, "Declined") }, leaders.Select(l => (l.GetProperty("publicId").GetGuid(), l.GetProperty("response").GetString()!)).ToArray());

        // An appointment that is over needs nobody, though it had one leader of two.
        Assert.False((await AgendaAsync("history")).EnumerateArray().Last().GetProperty("needsLeaders").GetBoolean());

        using var invalid = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/appointments?view=everything");
        await AssertRefusedAsync(invalid, HttpStatusCode.BadRequest, "invalid_view");
    }

    [Fact]
    public async Task ALeaderRemovedIsNoLongerOnTheAgenda()
    {
        var type = await TypeAsync();
        var leader = await LeaderAsync("Ven. Chandra");
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, leader.Id);

        using (var removed = await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{leader.PublicId}"))
        {
            await AssertOkAsync(removed, AppointmentStatus.Approved);
        }

        Assert.Empty(Assert.Single((await AgendaAsync()).EnumerateArray()).GetProperty("leaders").EnumerateArray());
    }

    // ------------------------------------------------------------------ assigning and removing

    [Fact]
    public async Task AssigningOverHttpApprovesAndNamesWhoIsNotFree()
    {
        var type = await TypeAsync();
        var (a, b) = (await LeaderAsync("Ven. Dhamma"), await LeaderAsync("Ven. Ehi"));
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        var (_, clash) = await AppointmentAsync(type, At(Day, 10));

        using (var assigned = await AssignAsync(no, a.PublicId, b.PublicId))
        {
            await AssertOkAsync(assigned, AppointmentStatus.Approved);
        }

        Assert.Equal(8, await BlocksAsync(id, a.Id));

        using (var busy = await AssignAsync(clash, a.PublicId))
        {
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
            var body = await BodyOf(busy);
            Assert.Equal(ServiceErrors.LeaderNotFree, body.GetProperty("code").GetString());
            Assert.Equal("Ven. Dhamma", Assert.Single(body.GetProperty("leaders").EnumerateArray()).GetString());
        }

        using (var none = await AssignAsync(clash))
        {
            await AssertRefusedAsync(none, HttpStatusCode.BadRequest, AppointmentResult.InvalidDetails);
        }
    }

    [Fact]
    public async Task RemovingALeaderFreesOnlyTheirTimeAndTellsThem()
    {
        var type = await TypeAsync();
        var (a, b) = (await LeaderAsync("Ven. Gnana"), await LeaderAsync("Ven. Hema"));
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, a.Id);
        await AssignDirectAsync(id, b.Id);

        using (var removed = await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{a.PublicId}"))
        {
            await AssertOkAsync(removed, AppointmentStatus.Approved);
        }

        Assert.Equal(AssignmentResponse.Removed, (await AnswerOfAsync(id, a.Id)).Response);
        Assert.Equal(0, await BlocksAsync(id, a.Id));
        Assert.Equal(8, await BlocksAsync(id, b.Id));
        Assert.Equal(1, await OutboxCountAsync(no, AppointmentDecisionHandler.RemovedEvent));
        Assert.Equal(
            a.Id,
            JsonDocument.Parse(await ScalarAsync<string>("SELECT Payload FROM core.OutboxMessages WHERE AggregateId = @no AND EventType = 'LeaderRemoved';", new { no }))
                .RootElement.GetProperty("leaderId").GetInt32());

        using (var again = await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{a.PublicId}"))
        {
            await AssertRefusedAsync(again, HttpStatusCode.Conflict, ServiceErrors.NotAssigned);
        }

        using var stranger = await SendAsync(Alpha(_alphaOwner), HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{Guid.CreateVersion7()}");
        await AssertRefusedAsync(stranger, HttpStatusCode.Conflict, ServiceErrors.NotAssigned);
    }

    // ------------------------------------------------------------------ answers

    [Fact]
    public async Task AnAdministratorAnswersForALeaderWhoDoesNotSignIn()
    {
        var type = await TypeAsync();
        var (a, b) = (await LeaderAsync("Ven. Indra"), await LeaderAsync("Ven. Jina"));
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, a.Id);
        await AssignDirectAsync(id, b.Id);

        using (var accepted = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/leaders/{a.PublicId}/respond", new { accept = true }))
        {
            await AssertOkAsync(accepted, AppointmentStatus.Approved);
        }

        Assert.Equal<(string, bool, Guid?, string?)>((AssignmentResponse.Accepted, true, _alphaOwner, null), await AnswerOfAsync(id, a.Id));
        Assert.Equal(8, await BlocksAsync(id, a.Id));

        using (var declined = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/leaders/{b.PublicId}/respond", new { accept = false, reason = "  At a funeral  " }))
        {
            await AssertOkAsync(declined, AppointmentStatus.Approved);
        }

        Assert.Equal<(string, bool, Guid?, string?)>((AssignmentResponse.Declined, true, _alphaOwner, "At a funeral"), await AnswerOfAsync(id, b.Id));
        Assert.Equal(0, await BlocksAsync(id, b.Id));
        Assert.Equal(8, await BlocksAsync(id, a.Id));
        Assert.Equal(1, await OutboxCountAsync(no, AppointmentDecisionHandler.DeclinedEvent));
        Assert.Equal(0, await OutboxCountAsync(no, AppointmentDecisionHandler.RemovedEvent));
        Assert.True(Assert.Single((await AgendaAsync("needs-leaders")).EnumerateArray()).GetProperty("needsLeaders").GetBoolean());

        using var missing = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/leaders/{a.PublicId}/respond", new { reason = "?" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("accept", Assert.Single((await BodyOf(missing)).GetProperty("fields").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task ALeaderSeesTheirOwnScheduleAndAnswersForThemselves()
    {
        var type = await TypeAsync();
        var self = await LeaderAsync("Ven. Kusala", _alphaLeaderUser);
        var other = await LeaderAsync("Ven. Lalith");
        var (mineId, mine) = await AppointmentAsync(type, At(Day, 9));
        var (laterId, later) = await AppointmentAsync(type, At(Day.AddDays(1), 9));
        var (othersId, _) = await AppointmentAsync(type, At(Day, 14));
        await AssignDirectAsync(mineId, self.Id);
        await AssignDirectAsync(laterId, self.Id);
        await AssignDirectAsync(othersId, other.Id);

        using (var schedule = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Get, "/api/institution/schedule"))
        {
            Assert.Equal(HttpStatusCode.OK, schedule.StatusCode);
            var items = (await BodyOf(schedule)).EnumerateArray().ToList();
            Assert.Equal(new[] { mine, later }, items.Select(i => i.GetProperty("appointmentNo").GetString()!).ToArray());
            Assert.Equal(AssignmentResponse.Pending, items[0].GetProperty("response").GetString());
            Assert.Equal("Appointments Donor", items[0].GetProperty("donorName").GetString());
        }

        using (var accepted = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Post, $"/api/institution/schedule/{mine}/respond", new { accept = true }))
        {
            await AssertOkAsync(accepted, AppointmentStatus.Approved);
        }

        Assert.Equal<(string, bool, Guid?, string?)>((AssignmentResponse.Accepted, false, _alphaLeaderUser, null), await AnswerOfAsync(mineId, self.Id));

        using (var declined = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Post, $"/api/institution/schedule/{later}/respond", new { accept = false }))
        {
            await AssertOkAsync(declined, AppointmentStatus.Approved);
        }

        Assert.Equal(0, await BlocksAsync(laterId, self.Id));

        // A declined appointment leaves the schedule; one assigned to someone else was never on it.
        using (var after = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Get, "/api/institution/schedule"))
        {
            Assert.Equal(new[] { mine }, (await BodyOf(after)).EnumerateArray().Select(i => i.GetProperty("appointmentNo").GetString()!).ToArray());
        }

        var (notTheirsId, notTheirs) = await AppointmentAsync(type, At(Day, 18));
        await AssignDirectAsync(notTheirsId, other.Id);
        using var refused = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Post, $"/api/institution/schedule/{notTheirs}/respond", new { accept = true });
        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, ServiceErrors.NotAssigned);
    }

    [Fact]
    public async Task OnlyAnAccountOnTheRosterAnswersAsALeader()
    {
        var type = await TypeAsync();
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, (await LeaderAsync("Ven. Mahinda")).Id);

        // Holds the LEADER role, but no roster row links the account.
        var unlinked = await MemberAsync(database.AlphaInstitutionId, "LEADER");
        using (var refused = await SendAsync(Alpha(unlinked), HttpMethod.Post, $"/api/institution/schedule/{no}/respond", new { accept = true }))
        {
            await AssertRefusedAsync(refused, HttpStatusCode.Forbidden, AppointmentResult.NotALeader);
        }

        using (var empty = await SendAsync(Alpha(unlinked), HttpMethod.Get, "/api/institution/schedule"))
        {
            Assert.Empty((await BodyOf(empty)).EnumerateArray());
        }

        // A leader of Beta's roster linked to the same account reaches nothing at Alpha.
        await LeaderAsync("Ven. Beta", unlinked, database.BetaInstitutionId);
        using var stillRefused = await SendAsync(Alpha(unlinked), HttpMethod.Post, $"/api/institution/schedule/{no}/respond", new { accept = true });
        await AssertRefusedAsync(stillRefused, HttpStatusCode.Forbidden, AppointmentResult.NotALeader);
    }

    [Fact]
    public async Task TheLeaderIsFoundOnTheRosterOfTheInstitutionActedFor()
    {
        // In an institution session RLS already hides other rosters. The handler names the
        // institution itself, so it holds in a session that sees every roster too.
        var type = await TypeAsync();
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, (await LeaderAsync("Ven. Vajira")).Id);
        var account = await MemberAsync(database.AlphaInstitutionId, "LEADER");
        await LeaderAsync("Ven. Beta Only", account, database.BetaInstitutionId);

        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.Platform()))
            .Options);

        var result = await new AppointmentDecisionHandler(context, TimeProvider.System)
            .RespondAsLeaderAsync(database.AlphaInstitutionId, no, accept: true, reason: null, account);

        Assert.Equal(AppointmentResult.NotALeader, result.Error);
    }

    // ------------------------------------------------------------------ decisions

    [Fact]
    public async Task ARequestIsRejectedWithAReasonTheDonorWillRead()
    {
        var type = await TypeAsync();
        var (id, no) = await AppointmentAsync(type, At(Day, 9));

        using (var noReason = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/reject", new { reason = "  " }))
        {
            await AssertRefusedAsync(noReason, HttpStatusCode.BadRequest, ServiceErrors.ReasonRequired);
        }

        using (var rejected = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/reject", new { reason = "All monks are away that week" }))
        {
            await AssertOkAsync(rejected, AppointmentStatus.Rejected);
        }

        Assert.Equal("All monks are away that week", await ScalarAsync<string>("SELECT RejectReason FROM booking.ServiceAppointments WHERE Id = @id;", new { id }));
        Assert.Equal(1, await OutboxCountAsync(no, AppointmentDecisionHandler.RejectedEvent));

        var (approvedId, approved) = await AppointmentAsync(type, At(Day, 14));
        await AssignDirectAsync(approvedId, (await LeaderAsync("Ven. Nanda")).Id);
        using var late = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{approved}/reject", new { reason = "Changed our mind" });
        await AssertRefusedAsync(late, HttpStatusCode.Conflict, ServiceErrors.InvalidTransition);
    }

    [Fact]
    public async Task TheInstitutionCancelsAndEveryLeadersTimeIsFreedAtOnce()
    {
        var type = await TypeAsync();
        var (a, b) = (await LeaderAsync("Ven. Ole"), await LeaderAsync("Ven. Piya"));
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, a.Id);
        await AssignDirectAsync(id, b.Id);
        var (otherId, _) = await AppointmentAsync(type, At(Day.AddDays(1), 9));
        await AssignDirectAsync(otherId, a.Id);

        using (var noReason = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/cancel"))
        {
            await AssertRefusedAsync(noReason, HttpStatusCode.BadRequest, ServiceErrors.ReasonRequired);
        }

        using (var cancelled = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/cancel", new { reason = "The hall is flooded" }))
        {
            await AssertOkAsync(cancelled, AppointmentStatus.Cancelled);
        }

        // In the institution's own transaction, not left to the outbox (V16).
        Assert.Equal(0, await BlocksAsync(id));
        Assert.Equal(8, await BlocksAsync(otherId));

        var payload = JsonElement.Parse(await ScalarAsync<string>("SELECT Payload FROM core.OutboxMessages WHERE AggregateId = @no AND EventType = 'AppointmentCancelled';", new { no }));
        Assert.False(payload.GetProperty("byDonor").GetBoolean());
    }

    [Fact]
    public async Task AnAppointmentIsCompletedOnlyOnceItHasEnded()
    {
        var type = await TypeAsync();
        var leader = await LeaderAsync("Ven. Rathana");
        var (soonId, soon) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(soonId, leader.Id);
        var (pastId, past) = await AppointmentAsync(type, At(Day.AddDays(-12), 9));
        await AssignDirectAsync(pastId, leader.Id);

        using (var early = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{soon}/complete"))
        {
            await AssertRefusedAsync(early, HttpStatusCode.Conflict, ServiceErrors.TooEarly);
        }

        using (var done = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{past}/complete"))
        {
            await AssertOkAsync(done, AppointmentStatus.Completed);
        }

        Assert.NotNull(await ScalarAsync<DateTime?>("SELECT CompletedAt FROM booking.ServiceAppointments WHERE Id = @pastId;", new { pastId }));
    }

    // ------------------------------------------------------------------ who may

    [Fact]
    public async Task EachActionNeedsItsOwnPermission()
    {
        var type = await TypeAsync();
        var leader = await LeaderAsync("Ven. Sumana");
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, leader.Id);

        // Staff hold the views only; a leader holds only their own schedule.
        Assert.Single((await AgendaAsync(member: _alphaStaff)).EnumerateArray());

        foreach (var member in new[] { _alphaStaff, _alphaLeaderUser })
        {
            foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                     {
                         (HttpMethod.Post, $"/api/institution/appointments/{no}/leaders", new { leaders = new[] { leader.PublicId } }),
                         (HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{leader.PublicId}", null),
                         (HttpMethod.Post, $"/api/institution/appointments/{no}/leaders/{leader.PublicId}/respond", new { accept = true }),
                         (HttpMethod.Post, $"/api/institution/appointments/{no}/reject", new { reason = "no" }),
                         (HttpMethod.Post, $"/api/institution/appointments/{no}/cancel", new { reason = "no" }),
                         (HttpMethod.Post, $"/api/institution/appointments/{no}/complete", null),
                     })
            {
                using var response = await SendAsync(Alpha(member), method, path, body);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        using (var leaderAgenda = await SendAsync(Alpha(_alphaLeaderUser), HttpMethod.Get, "/api/institution/appointments"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, leaderAgenda.StatusCode);
        }

        // The owner runs the institution but is no leader: the schedule is not theirs.
        using var ownerSchedule = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/schedule");
        Assert.Equal(HttpStatusCode.Forbidden, ownerSchedule.StatusCode);
    }

    [Fact]
    public async Task AnotherInstitutionsAppointmentIsNotFound()
    {
        // Asked for by Beta's owner as a donor, so RLS's owner branch shows it to their Beta session
        // (V9): only the handler's own check keeps them from acting on it as Beta.
        var leader = await LeaderAsync("Ven. Tissa");
        var (id, no) = await AppointmentAsync(await TypeAsync(), At(Day, 9), donor: _betaOwner);
        await AssignDirectAsync(id, leader.Id);

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Delete, $"/api/institution/appointments/{no}/leaders/{leader.PublicId}", null),
                     (HttpMethod.Post, $"/api/institution/appointments/{no}/reject", new { reason = "no" }),
                     (HttpMethod.Post, $"/api/institution/appointments/{no}/cancel", new { reason = "no" }),
                     (HttpMethod.Post, $"/api/institution/appointments/{no}/complete", null),
                 })
        {
            using var response = await SendAsync(ClientFor(_betaOwner, database.BetaInstitutionId), method, path, body);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, $"{method} {path}: {response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // Removing names a leader by public id; Alpha's leader is not on Beta's roster.
                await AssertRefusedAsync(response, HttpStatusCode.Conflict, ServiceErrors.NotAssigned);
            }
            else
            {
                await AssertRefusedAsync(response, HttpStatusCode.NotFound, AppointmentResult.NotFound);
            }
        }

        using (var betaAgenda = await SendAsync(ClientFor(_betaOwner, database.BetaInstitutionId), HttpMethod.Get, "/api/institution/appointments"))
        {
            Assert.Empty((await BodyOf(betaAgenda)).EnumerateArray());
        }

        Assert.Equal(AppointmentStatus.Approved, await ScalarAsync<string>("SELECT Status FROM booking.ServiceAppointments WHERE Id = @id;", new { id }));
        Assert.Equal(8, await BlocksAsync(id));
    }

    // ------------------------------------------------------------------ at once

    [Fact]
    public async Task ACancellationAndADeclineAtOnce_OnlyOneIsWritten()
    {
        var type = await TypeAsync();
        var leader = await LeaderAsync("Ven. Upali");
        var (id, no) = await AppointmentAsync(type, At(Day, 9));
        await AssignDirectAsync(id, leader.Id);

        HttpResponseMessage[] responses;
        await using (var holder = await PlatformConnectionAsync())
        {
            await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
            await holder.ExecuteAsync("SELECT Id FROM booking.ServiceAppointments WITH (UPDLOCK, ROWLOCK) WHERE Id = @id;", new { id }, hold);

            var running = new[]
            {
                Task.Run(() => SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/cancel", new { reason = "Flooded" })),
                Task.Run(() => SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/appointments/{no}/leaders/{leader.PublicId}/respond", new { accept = false })),
            };

            await using (var observer = new SqlConnection(database.ConnectionString))
            {
                await observer.OpenAsync();
                await WaitForWaitersAsync(observer, "booking.ServiceAppointments", 2);
            }

            await hold.CommitAsync();
            responses = await Task.WhenAll(running);
        }

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).Order().ToArray());
        await AssertRefusedAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, AppointmentResult.Stale);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        // Whichever won, the leader's time is free and only the winner's message was written.
        Assert.Equal(0, await BlocksAsync(id));
        Assert.Equal(1, await OutboxCountAsync(no, AppointmentDecisionHandler.CancelledEvent) + await OutboxCountAsync(no, AppointmentDecisionHandler.DeclinedEvent));
    }

    private static async Task WaitForWaitersAsync(SqlConnection observer, string table, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        int waiting;
        do
        {
            waiting = await observer.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.dm_tran_locks l
                JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID() AND p.object_id = OBJECT_ID(@table);
                """,
                new { table });

            if (waiting >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Only {waiting} of {expected} callers queued on {table}: the race was not forced.");
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Services;

/// <summary>
/// The donor's side of religious services over HTTP (Phase E, 8.5), against real SQL Server and
/// Redis: the services on an institution's public page, asking for one, the donor's own list, and
/// cancelling, with the leaders' time given back by the outbox (V16).
/// </summary>
/// <remarks>
/// Alpha has SERVICES on, as approval turns it on for a temple (V12); Beta has not. Service types,
/// leaders and assignments are written straight into the tables: their own endpoints are tested in
/// <see cref="ServiceSetupEndpointsTests"/> and <see cref="LeaderAssignmentTests"/>.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class DonorAppointmentEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "donor-appointment-test-signing-key-at-least-32-bytes";

    /// <summary>The rule name of <c>BookingRateLimits.RequestServicePerUser</c>, which is internal to the Api.</summary>
    private const string RequestRule = "appointment-create-user";

    private readonly List<Guid> _users = [];
    private readonly List<string> _rateLimitKeys = [];
    private WebApplicationFactory<Program> _host = null!;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    /// <summary>Ten days ahead in Colombo: inside the default types' 1-30 day window.</summary>
    private static DateOnly Day => Today.AddDays(10);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @alpha, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m WHERE m.Code = 'SERVICES';
            """,
            new { alpha = database.AlphaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackProxyStartupFilter()));
        });
    }

    public async Task DisposeAsync()
    {
        // The reseed clears every Phase E table (V24), so the users can go.
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM core.OutboxMessages WHERE AggregateType = 'ServiceAppointment';
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users });

        await using (var redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString))
        {
            await redis.GetDatabase().KeyDeleteAsync([.. _rateLimitKeys.Select(k => (RedisKey)k)]);
        }

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Every request arrives from loopback, the proxy ASP.NET Core trusts by default.</summary>
    private sealed class LoopbackProxyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

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

    /// <summary>A signed-up donor: the global DONOR role, and a confirmed phone unless said otherwise.</summary>
    private async Task<Guid> DonorAsync(bool phoneConfirmed = true)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Service Donor', @phone, @phoneConfirmed, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone, phoneConfirmed });

        _users.Add(id);
        _rateLimitKeys.Add(RateLimitKey.For(RequestRule, id.ToString()));
        return id;
    }

    /// <summary>A client for <paramref name="userId"/>, in their personal session unless an institution is named.</summary>
    private HttpClient ClientFor(Guid userId, int? institutionId = null)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
    }

    /// <summary>A client with no token, forwarded for a random address.</summary>
    private HttpClient Anonymous()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var address = $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}";
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        _rateLimitKeys.Add(RateLimitKey.For("institution-read-client", address));
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

    /// <summary>A service type of <paramref name="institutionId"/> (Alpha by default), written as the platform; its public id.</summary>
    private async Task<Guid> TypeAsync(
        string location = ServiceLocation.Either,
        int? institutionId = null,
        bool active = true,
        short minAdvanceDays = 1,
        short maxAdvanceDays = 30)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.ServiceTypes (PublicId, InstitutionId, Name, NameSi, Description, DurationMinutes, BufferMinutes,
                                         LeadersRequired, LocationMode, MinAdvanceDays, MaxAdvanceDays, IsActive, CreatedBy)
            VALUES (@publicId, @institutionId, @name, N'පිරිත්', N'Chanted through the night', 180, 30, 2,
                    @location, @minAdvanceDays, @maxAdvanceDays, @active, @createdBy);
            """,
            new
            {
                publicId,
                institutionId = institutionId ?? database.AlphaInstitutionId,
                name = "Pirith " + Guid.NewGuid().ToString("N")[..8],
                location,
                minAdvanceDays,
                maxAdvanceDays,
                active,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    private static object RequestBody(
        Guid type,
        DateOnly date,
        string startTime = "10:00",
        string location = ServiceLocation.AtInstitution,
        string? address = null,
        string? occasion = "Our new house",
        string? note = "Please bring three monks") => new
        {
            serviceType = type,
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            startTime,
            location,
            address,
            occasion,
            note,
        };

    private Task<HttpResponseMessage> RequestAsync(Guid donor, object body) =>
        SendAsync(ClientFor(donor), HttpMethod.Post, "/api/appointments", body);

    /// <summary>A request made through the API; its number.</summary>
    private async Task<string> RequestedAsync(Guid donor, Guid type, DateOnly? date = null, string startTime = "10:00")
    {
        using var response = await RequestAsync(donor, RequestBody(type, date ?? Day, startTime));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyOf(response)).GetProperty("appointmentNo").GetString()!;
    }

    private async Task<JsonElement> MineAsync(Guid donor)
    {
        using var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/appointments/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyOf(response);
    }

    private Task<HttpResponseMessage> CancelAsync(Guid donor, string appointmentNo, string? reason = "We are travelling") =>
        SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/appointments/{appointmentNo}/cancel", new { reason });

    /// <summary>An appointment written straight into the table, at any time and status; its id and number.</summary>
    private async Task<(long Id, string No)> AppointmentAsync(Guid donor, Guid type, DateTime startsAtUtc, string status = AppointmentStatus.Requested)
    {
        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        var id = await ScalarAsync<long>(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, Status, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Service Donor', '+94770000000',
                   @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt), 'AtInstitution', @status, @donor
            FROM booking.ServiceTypes WHERE PublicId = @type;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { no, donor, type, startsAt = startsAtUtc, status });
        return (id, no);
    }

    /// <summary>A leader on Alpha's roster; their internal id.</summary>
    private Task<int> LeaderAsync() =>
        ScalarAsync<int>(
            """
            INSERT org.ReligiousLeaders (PublicId, InstitutionId, DisplayName, IsActive, CreatedBy)
            VALUES (NEWID(), @institutionId, N'Ven. Test Thero', 1, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { institutionId = database.AlphaInstitutionId, createdBy = SethikaTestDatabase.AlphaUserId });

    /// <summary>What an assignment writes, written directly: the leader's row and their blocks, and the approval.</summary>
    private async Task AssignAsync(long appointmentId, int leaderId)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            UPDATE booking.ServiceAppointments SET Status = 'Approved', DecidedBy = @by, DecidedAt = SYSUTCDATETIME() WHERE Id = @appointmentId;
            INSERT booking.AppointmentLeaders (AppointmentId, LeaderId, InstitutionId, AssignedBy)
            VALUES (@appointmentId, @leaderId, @institutionId, @by);
            """,
            new { appointmentId, leaderId, institutionId = database.AlphaInstitutionId, by = SethikaTestDatabase.AlphaUserId });

        var (startsAt, endsAt) = await connection.QuerySingleAsync<(DateTime, DateTime)>(
            "SELECT StartsAt, EndsAt FROM booking.ServiceAppointments WHERE Id = @appointmentId;", new { appointmentId });

        await connection.ExecuteAsync(
            """
            INSERT booking.LeaderTimeBlocks (LeaderId, BlockStart, InstitutionId, Kind, AppointmentId)
            VALUES (@leaderId, @blockStart, @institutionId, 'Appointment', @appointmentId);
            """,
            TimeBlocks.For(startsAt, endsAt).Select(blockStart => new { leaderId, blockStart, institutionId = database.AlphaInstitutionId, appointmentId }));
    }

    private Task<int> BlocksOfAsync(long appointmentId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM booking.LeaderTimeBlocks WHERE AppointmentId = @appointmentId;", new { appointmentId });

    /// <summary>The outbox message written for the appointment, as the processor would hand it over.</summary>
    private async Task<OutboxEvent> OutboxOfAsync(string appointmentNo, string eventType)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<OutboxEvent>(
            """
            SELECT Id, EventType, AggregateType, AggregateId, Payload
            FROM core.OutboxMessages WHERE AggregateId = @appointmentNo AND EventType = @eventType;
            """,
            new { appointmentNo, eventType });
    }

    /// <summary>The handler the outbox runs, on a platform connection as the job gives it.</summary>
    private async Task HandleAsPlatformAsync(OutboxEvent message)
    {
        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.Platform()))
            .Options);

        var connections = new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform());
        var texts = new AppointmentTexts(new UserTextMessages(
            connections,
            new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance),
            NullLogger<UserTextMessages>.Instance));

        await new AppointmentCancelledHandler(context, new AppointmentNotificationQueries(connections), texts).HandleAsync(message, CancellationToken.None);
    }

    // ------------------------------------------------------------------ the public page

    [Fact]
    public async Task TheInstitutionsPageListsItsActiveServicesForTheDonorToChoose()
    {
        var pirith = await TypeAsync(minAdvanceDays: 2, maxAdvanceDays: 60);
        var retired = await TypeAsync(active: false);

        using var response = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions/alpha-temple");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var services = (await BodyOf(response)).GetProperty("services");
        var listed = Assert.Single(services.EnumerateArray());
        Assert.Equal(pirith, listed.GetProperty("publicId").GetGuid());
        Assert.NotEqual(retired, listed.GetProperty("publicId").GetGuid());
        Assert.Equal("පිරිත්", listed.GetProperty("nameSi").GetString());
        Assert.Equal("Chanted through the night", listed.GetProperty("description").GetString());
        Assert.Equal(180, listed.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(ServiceLocation.Either, listed.GetProperty("locationMode").GetString());
        Assert.Equal(2, listed.GetProperty("minAdvanceDays").GetInt32());
        Assert.Equal(60, listed.GetProperty("maxAdvanceDays").GetInt32());

        // Exactly what the donor chooses by. The institution's planning stays with it: no travel
        // buffer, no leader count, no internal id.
        Assert.Equal(
            ["description", "durationMinutes", "locationMode", "maxAdvanceDays", "minAdvanceDays", "name", "nameSi", "nameTa", "publicId"],
            listed.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AnInstitutionWithServicesSwitchedOffListsNone()
    {
        await TypeAsync(institutionId: database.BetaInstitutionId);
        await TypeAsync();

        using (var beta = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions/beta-temple"))
        {
            Assert.Equal(HttpStatusCode.OK, beta.StatusCode);
            Assert.Empty((await BodyOf(beta)).GetProperty("services").EnumerateArray());
        }

        await ExecuteAsync(
            """
            UPDATE org.InstitutionModules SET IsEnabled = 0
            WHERE InstitutionId = @alpha AND ModuleId = (SELECT Id FROM auth.Modules WHERE Code = 'SERVICES');
            """,
            new { alpha = database.AlphaInstitutionId });

        using var alpha = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions/alpha-temple");
        Assert.Empty((await BodyOf(alpha)).GetProperty("services").EnumerateArray());
    }

    // ------------------------------------------------------------------ asking

    [Fact]
    public async Task ADonorAsksForAServiceAtHomeAndItWaitsForTheInstitution()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();

        using var response = await RequestAsync(donor, RequestBody(type, Day, "10:00", ServiceLocation.AtDonorPlace, "  12 Temple Road, Galle  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        var no = body.GetProperty("appointmentNo").GetString()!;
        Assert.Matches($"^APT-{DateTime.UtcNow.Year}-\\d{{6}}$", no);
        Assert.Equal(AppointmentStatus.Requested, body.GetProperty("status").GetString());

        await using var connection = await PlatformConnectionAsync();
        var saved = await connection.QuerySingleAsync(
            """
            SELECT Id, StartsAt, EndsAt, LocationMode, Address, Occasion, DonorNote, DonorUserId, DonorName, InstitutionId
            FROM booking.ServiceAppointments WHERE AppointmentNo = @no;
            """,
            new { no });
        long id = saved.Id;

        // 10:00 in Colombo is 04:30 UTC; the end follows from the service's three hours.
        Assert.Equal(Day.ToDateTime(new TimeOnly(4, 30)), (DateTime)saved.StartsAt);
        Assert.Equal(Day.ToDateTime(new TimeOnly(7, 30)), (DateTime)saved.EndsAt);
        Assert.Equal(ServiceLocation.AtDonorPlace, (string)saved.LocationMode);
        Assert.Equal("12 Temple Road, Galle", (string)saved.Address);
        Assert.Equal("Our new house", (string)saved.Occasion);
        Assert.Equal("Please bring three monks", (string)saved.DonorNote);
        Assert.Equal(donor, (Guid)saved.DonorUserId);
        Assert.Equal("Service Donor", (string)saved.DonorName);
        Assert.Equal(database.AlphaInstitutionId, (int)saved.InstitutionId);

        var history = await connection.QuerySingleAsync<(string? FromStatus, string ToStatus, Guid ChangedBy)>(
            "SELECT FromStatus, ToStatus, ChangedBy FROM booking.AppointmentStatusHistory WHERE AppointmentId = @id;", new { id });
        Assert.Equal(((string?)null, AppointmentStatus.Requested, donor), history);

        var message = await OutboxOfAsync(no, RequestServiceHandler.RequestedEvent);
        Assert.Equal(nameof(ServiceAppointment), message.AggregateType);
        Assert.Equal(no, JsonElement.Parse(message.Payload).GetProperty("appointmentNo").GetString());

        // A request holds no leader: nothing is assigned and no time is blocked (section 4, flow 1).
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.AppointmentLeaders WHERE AppointmentId = @id;", new { id }));
        Assert.Equal(0, await BlocksOfAsync(id));
    }

    [Fact]
    public async Task AnEarlyMorningStartIsTheDayBeforeInUtc()
    {
        var donor = await DonorAsync();
        var no = await RequestedAsync(donor, await TypeAsync(), Day, "05:00");

        var startsAt = await ScalarAsync<DateTime>("SELECT StartsAt FROM booking.ServiceAppointments WHERE AppointmentNo = @no;", new { no });

        Assert.Equal(Day.AddDays(-1).ToDateTime(new TimeOnly(23, 30)), startsAt);
    }

    [Fact]
    public async Task AppointmentNumbersFollowOnePlatformWideSeries()
    {
        var first = await RequestedAsync(await DonorAsync(), await TypeAsync());
        var second = await RequestedAsync(await DonorAsync(), await TypeAsync());

        Assert.Equal(int.Parse(first[^6..], CultureInfo.InvariantCulture) + 1, int.Parse(second[^6..], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TheRequestFormNamesEveryFieldToFix()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();

        using (var empty = await RequestAsync(donor, new { serviceType = Guid.Empty, date = "20-11-2026", startTime = "10am" }))
        {
            await AssertFieldsAsync(empty, "serviceType", "date", "startTime");
        }

        using var wrong = await RequestAsync(
            donor, RequestBody(type, Day, location: ServiceLocation.Either, address: new string('a', 301), occasion: new string('o', 201), note: new string('n', 501)));
        await AssertFieldsAsync(wrong, "location", "address", "occasion", "note");

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.ServiceAppointments WHERE DonorUserId = @donor;", new { donor }));
    }

    [Fact]
    public async Task EveryRefusalSaysWhyAndSavesNothing()
    {
        var donor = await DonorAsync();
        var either = await TypeAsync();
        var templeOnly = await TypeAsync(ServiceLocation.AtInstitution);
        var soonest = await TypeAsync(minAdvanceDays: 3, maxAdvanceDays: 10);

        async Task RefusedAsync(object body, HttpStatusCode status, string code)
        {
            using var response = await RequestAsync(donor, body);
            await AssertRefusedAsync(response, status, code);
        }

        await RefusedAsync(RequestBody(Guid.CreateVersion7(), Day), HttpStatusCode.NotFound, RequestServiceResult.ServiceNotFound);
        await RefusedAsync(RequestBody(await TypeAsync(active: false), Day), HttpStatusCode.Conflict, ServiceErrors.ServiceInactive);
        await RefusedAsync(RequestBody(either, Day, "10:10"), HttpStatusCode.BadRequest, ServiceErrors.NotOnTheGrid);
        await RefusedAsync(RequestBody(templeOnly, Day, location: ServiceLocation.AtDonorPlace, address: "12 Temple Road"), HttpStatusCode.Conflict, ServiceErrors.LocationNotOffered);
        await RefusedAsync(RequestBody(either, Day, location: ServiceLocation.AtDonorPlace, address: "   "), HttpStatusCode.BadRequest, ServiceErrors.AddressRequired);
        await RefusedAsync(RequestBody(soonest, Today.AddDays(2)), HttpStatusCode.Conflict, ServiceErrors.TooSoon);
        await RefusedAsync(RequestBody(soonest, Today.AddDays(11)), HttpStatusCode.Conflict, ServiceErrors.TooFar);

        // The window counts Colombo dates: 05:00 on the tenth day is still the ninth in UTC, and is
        // in; 05:00 on the eleventh day is the tenth in UTC, and is out.
        await RefusedAsync(RequestBody(soonest, Today.AddDays(11), "05:00"), HttpStatusCode.Conflict, ServiceErrors.TooFar);
        await RequestedAsync(donor, soonest, Today.AddDays(10), "05:00");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.ServiceAppointments WHERE DonorUserId = @donor;", new { donor }));
    }

    [Fact]
    public async Task AnInstitutionThatIsNotActiveOrHasServicesOffTakesNoRequests()
    {
        var donor = await DonorAsync();

        using (var beta = await RequestAsync(donor, RequestBody(await TypeAsync(institutionId: database.BetaInstitutionId), Day)))
        {
            await AssertRefusedAsync(beta, HttpStatusCode.Conflict, ServiceErrors.ServiceInactive);
        }

        var alphaType = await TypeAsync();
        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Id = @alpha;", new { alpha = database.AlphaInstitutionId });

        using var suspended = await RequestAsync(donor, RequestBody(alphaType, Day));
        await AssertRefusedAsync(suspended, HttpStatusCode.Conflict, ServiceErrors.InstitutionNotActive);
    }

    [Fact]
    public async Task ADonorWithoutAConfirmedPhoneCannotAsk()
    {
        using var response = await RequestAsync(await DonorAsync(phoneConfirmed: false), RequestBody(await TypeAsync(), Day));
        await AssertRefusedAsync(response, HttpStatusCode.Conflict, RequestServiceResult.PhoneRequired);
    }

    [Fact]
    public async Task OnlyASignedInDonorInTheirPersonalSessionMayAskListOrCancel()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();
        var no = await RequestedAsync(donor, type);

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Post, "/api/appointments", RequestBody(type, Day)),
                     (HttpMethod.Get, "/api/appointments/mine", null),
                     (HttpMethod.Post, $"/api/appointments/{no}/cancel", null),
                 })
        {
            using (var anonymous = await SendAsync(Anonymous(), method, path, body))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }

            // The DONOR role is global: a session switched into an institution does not hold it (plan 02, B1).
            using var switched = await SendAsync(ClientFor(donor, database.AlphaInstitutionId), method, path, body);
            Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        }
    }

    [Fact]
    public async Task AskingIsRateLimitedPerDonor()
    {
        var donor = await DonorAsync();
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString))
        {
            await redis.GetDatabase().StringSetAsync(RateLimitKey.For(RequestRule, donor.ToString()), 10, TimeSpan.FromHours(1));
        }

        using var response = await RequestAsync(donor, RequestBody(await TypeAsync(), Day));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // ------------------------------------------------------------------ the donor's list

    [Fact]
    public async Task MyListShowsMyOwnRequestsLatestFirstAndWhetherICanStillCancel()
    {
        var donor = await DonorAsync();
        var other = await DonorAsync();
        var type = await TypeAsync();

        var later = await RequestedAsync(donor, type, Day.AddDays(1), "18:15");
        var sooner = await RequestedAsync(donor, type, Day);
        await RequestedAsync(other, type);

        // Today's, written directly (no request may be made for today): the donor can no longer cancel it.
        var (_, today) = await AppointmentAsync(donor, type, SriLankaTime.ToUtc(Today, new TimeOnly(23, 45)));

        var mine = (await MineAsync(donor)).EnumerateArray().ToList();

        Assert.Equal(new[] { later, sooner, today }, mine.Select(a => a.GetProperty("appointmentNo").GetString()!).ToArray());

        var first = mine[0];
        Assert.Equal(AppointmentStatus.Requested, first.GetProperty("status").GetString());
        Assert.Equal(SriLankaTime.ToUtc(Day.AddDays(1), new TimeOnly(18, 15)), first.GetProperty("startsAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(SriLankaTime.ToUtc(Day.AddDays(1), new TimeOnly(21, 15)), first.GetProperty("endsAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal("alpha-temple", first.GetProperty("institution").GetString());
        Assert.Equal("Alpha Temple", first.GetProperty("institutionName").GetString());
        Assert.Equal(type, first.GetProperty("serviceType").GetGuid());
        Assert.Equal("පිරිත්", first.GetProperty("serviceNameSi").GetString());
        Assert.Equal(ServiceLocation.AtInstitution, first.GetProperty("locationMode").GetString());
        Assert.Equal("Our new house", first.GetProperty("occasion").GetString());
        Assert.Equal("Please bring three monks", first.GetProperty("note").GetString());
        Assert.True(first.GetProperty("canCancel").GetBoolean());
        Assert.False(mine[2].GetProperty("canCancel").GetBoolean());

        // The roster stays inside the institution (V9).
        Assert.False(first.TryGetProperty("leaders", out _));
    }

    [Fact]
    public async Task MyListNamesTheDonorEvenWhereRlsWouldShowTheInstitutionsRequests()
    {
        // A staff member's institution session sees all of Alpha's appointments through RLS's tenant
        // branch; the list must still hold only their own.
        var staff = await DonorAsync();
        var type = await TypeAsync();
        var theirs = await RequestedAsync(staff, type);
        await RequestedAsync(await DonorAsync(), type);

        var queries = new DonorAppointmentQueries(new SqlConnectionFactory(
            database.ConnectionString, new TestCurrentUserContext { UserId = staff, InstitutionId = database.AlphaInstitutionId }));

        var mine = await queries.ListMineAsync(staff, DateTime.UtcNow);

        Assert.Equal(theirs, Assert.Single(mine).AppointmentNo);
    }

    // ------------------------------------------------------------------ cancelling

    [Fact]
    public async Task ADonorCancelsAndTheOutboxGivesTheLeadersTimeBack()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();
        var no = await RequestedAsync(donor, type);
        var id = await ScalarAsync<long>("SELECT Id FROM booking.ServiceAppointments WHERE AppointmentNo = @no;", new { no });

        var leader = await LeaderAsync();
        await AssignAsync(id, leader);
        var (elsewhere, _) = await AppointmentAsync(await DonorAsync(), type, SriLankaTime.ToUtc(Day.AddDays(1), new TimeOnly(10, 0)));
        await AssignAsync(elsewhere, leader);
        var blocks = await BlocksOfAsync(id);
        Assert.Equal(12, blocks);

        using (var response = await CancelAsync(donor, no, "  We are travelling  "))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await BodyOf(response);
            Assert.Equal(no, body.GetProperty("appointmentNo").GetString());
            Assert.Equal(AppointmentStatus.Cancelled, body.GetProperty("status").GetString());
        }

        await using (var connection = await PlatformConnectionAsync())
        {
            var saved = await connection.QuerySingleAsync<(string Status, Guid CancelledBy, string CancelReason)>(
                "SELECT Status, CancelledBy, CancelReason FROM booking.ServiceAppointments WHERE Id = @id;", new { id });
            Assert.Equal((AppointmentStatus.Cancelled, donor, "We are travelling"), saved);

            var history = await connection.QuerySingleAsync<(string FromStatus, string ToStatus, Guid ChangedBy)>(
                "SELECT FromStatus, ToStatus, ChangedBy FROM booking.AppointmentStatusHistory WHERE AppointmentId = @id AND ToStatus = 'Cancelled';", new { id });
            Assert.Equal((AppointmentStatus.Approved, AppointmentStatus.Cancelled, donor), history);
        }

        // The donor's session cannot see the leaders' blocks (V9), so they wait for the outbox (V16).
        Assert.Equal(blocks, await BlocksOfAsync(id));

        var message = await OutboxOfAsync(no, AppointmentDecisionHandler.CancelledEvent);
        Assert.True(JsonElement.Parse(message.Payload).GetProperty("byDonor").GetBoolean());

        await HandleAsPlatformAsync(message);
        Assert.Equal(0, await BlocksOfAsync(id));
        Assert.Equal(blocks, await BlocksOfAsync(elsewhere));

        // Handed over again, as after a crash: nothing left to do, and no error.
        await HandleAsPlatformAsync(message);
        Assert.Equal(blocks, await BlocksOfAsync(elsewhere));

        var listed = Assert.Single((await MineAsync(donor)).EnumerateArray());
        Assert.Equal(AppointmentStatus.Cancelled, listed.GetProperty("status").GetString());
        Assert.Equal("We are travelling", listed.GetProperty("cancelReason").GetString());
        Assert.False(listed.GetProperty("canCancel").GetBoolean());
    }

    [Fact]
    public async Task TheOutboxNeverFreesTheTimeOfAnAppointmentStillGoing()
    {
        var donor = await DonorAsync();
        var (id, no) = await AppointmentAsync(donor, await TypeAsync(), SriLankaTime.ToUtc(Day, new TimeOnly(10, 0)));
        await AssignAsync(id, await LeaderAsync());

        var stray = new OutboxEvent(0, AppointmentDecisionHandler.CancelledEvent, nameof(ServiceAppointment), no, JsonSerializer.Serialize(new { appointmentNo = no }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsPlatformAsync(stray));
        Assert.Equal(12, await BlocksOfAsync(id));
    }

    [Fact]
    public void TheHostDispatchesEachAppointmentEventToItsHandler()
    {
        using var scope = _host.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToList();

        var handler = Assert.Single(handlers, h => h.EventTypes.Contains(AppointmentDecisionHandler.CancelledEvent));
        Assert.IsType<AppointmentCancelledHandler>(handler);

        // The texts (8.8): every other appointment event that tells someone, and none for a decline,
        // which the institution sees on its agenda instead.
        foreach (var eventType in new[]
                 {
                     RequestServiceHandler.RequestedEvent, AssignLeadersHandler.ApprovedEvent, AppointmentDecisionHandler.RejectedEvent,
                     AssignLeadersHandler.AssignedEvent, AppointmentDecisionHandler.RemovedEvent,
                 })
        {
            Assert.IsType<AppointmentSmsHandler>(Assert.Single(handlers, h => h.EventTypes.Contains(eventType)));
        }

        Assert.DoesNotContain(handlers, h => h.EventTypes.Contains(AppointmentDecisionHandler.DeclinedEvent));
    }

    [Fact]
    public async Task TheDonorMayCancelUntilTheEndOfTheDayBeforeInColombo()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();

        // 05:00 tomorrow in Colombo is still today in UTC: by Colombo dates it is tomorrow, so it may go.
        var (_, tomorrow) = await AppointmentAsync(donor, type, SriLankaTime.ToUtc(Today.AddDays(1), new TimeOnly(5, 0)));
        var (_, today) = await AppointmentAsync(donor, type, SriLankaTime.ToUtc(Today, new TimeOnly(23, 45)));

        using (var closed = await CancelAsync(donor, today))
        {
            await AssertRefusedAsync(closed, HttpStatusCode.Conflict, ServiceErrors.CancelWindowClosed);
        }

        using var open = await CancelAsync(donor, tomorrow);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    [Fact]
    public async Task SomeoneElsesAppointmentIsNotFound()
    {
        var donor = await DonorAsync();
        var no = await RequestedAsync(donor, await TypeAsync());

        using (var stranger = await CancelAsync(await DonorAsync(), no))
        {
            await AssertRefusedAsync(stranger, HttpStatusCode.NotFound, AppointmentResult.NotFound);
        }

        using (var missing = await CancelAsync(donor, "APT-2026-999999"))
        {
            await AssertRefusedAsync(missing, HttpStatusCode.NotFound, AppointmentResult.NotFound);
        }

        Assert.Equal(AppointmentStatus.Requested, await ScalarAsync<string>("SELECT Status FROM booking.ServiceAppointments WHERE AppointmentNo = @no;", new { no }));
    }

    [Fact]
    public async Task StaffWhoSeeTheAppointmentThroughTheirInstitutionCannotCancelItAsTheDonor()
    {
        // Over HTTP the donor endpoints refuse an institution session (403), and in a personal
        // session RLS hides other donors' rows. The handler's own check is what holds if it is ever
        // called in an institution session, where the tenant branch of RLS shows the row.
        var no = await RequestedAsync(await DonorAsync(), await TypeAsync());
        var staff = await DonorAsync();

        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(
                new TestCurrentUserContext { UserId = staff, InstitutionId = database.AlphaInstitutionId }))
            .Options);

        Assert.True(await context.ServiceAppointments.AnyAsync(a => a.AppointmentNo == no));

        var result = await new AppointmentDecisionHandler(context, TimeProvider.System).CancelAsDonorAsync(no, null, staff);

        Assert.Equal(AppointmentResult.NotFound, result.Error);
        Assert.Equal(AppointmentStatus.Requested, await ScalarAsync<string>("SELECT Status FROM booking.ServiceAppointments WHERE AppointmentNo = @no;", new { no }));
    }

    [Fact]
    public async Task AnAppointmentEndedOrCancelledCannotBeCancelledAgain()
    {
        var donor = await DonorAsync();
        var type = await TypeAsync();
        var no = await RequestedAsync(donor, type);

        using (var tooLong = await CancelAsync(donor, no, new string('r', 301)))
        {
            await AssertRefusedAsync(tooLong, HttpStatusCode.BadRequest, ServiceErrors.ReasonTooLong);
        }

        using (var first = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/appointments/{no}/cancel"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var again = await CancelAsync(donor, no))
        {
            await AssertRefusedAsync(again, HttpStatusCode.Conflict, ServiceErrors.InvalidTransition);
        }

        var (_, done) = await AppointmentAsync(donor, type, SriLankaTime.ToUtc(Day, new TimeOnly(8, 0)), AppointmentStatus.Completed);
        using var completed = await CancelAsync(donor, done);
        await AssertRefusedAsync(completed, HttpStatusCode.Conflict, ServiceErrors.InvalidTransition);
    }

    [Fact]
    public async Task TwoCancellationsAtOnce_OnlyOneIsWritten()
    {
        var donor = await DonorAsync();
        var no = await RequestedAsync(donor, await TypeAsync());
        var id = await ScalarAsync<long>("SELECT Id FROM booking.ServiceAppointments WHERE AppointmentNo = @no;", new { no });

        // Both read the row, then queue behind an update lock to write it; the first to go changes
        // its RowVersion, so the second's UPDATE matches nothing.
        HttpResponseMessage[] responses;
        await using (var holder = await PlatformConnectionAsync())
        {
            await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
            await holder.ExecuteAsync("SELECT Id FROM booking.ServiceAppointments WITH (UPDLOCK, ROWLOCK) WHERE Id = @id;", new { id }, hold);

            var running = Enumerable.Range(0, 2).Select(_ => Task.Run(() => CancelAsync(donor, no))).ToArray();

            await using (var observer = new SqlConnection(database.ConnectionString))
            {
                await observer.OpenAsync();
                await WaitForWaitersAsync(observer, "booking.ServiceAppointments", 2);
            }

            await hold.CommitAsync();
            responses = await Task.WhenAll(running);
        }

        var statuses = responses.Select(r => r.StatusCode).Order().ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, statuses);
        await AssertRefusedAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, AppointmentResult.Stale);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.AppointmentStatusHistory WHERE AppointmentId = @id AND ToStatus = 'Cancelled';", new { id }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateId = @no AND EventType = 'AppointmentCancelled';", new { no }));
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

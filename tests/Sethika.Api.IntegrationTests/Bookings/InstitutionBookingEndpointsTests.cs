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
using Sethika.Application.Bookings.Engine;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// The institution's booking API over HTTP (plan 03, 5.4), against real SQL Server and Redis: who
/// may do what, that one institution never reaches another's slots or bookings, the slot editor's
/// <c>RowVersion</c>, and that a rejection or cancellation gives the place back.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them, so the
/// permissions are the catalogue's own. The test data gives each institution one slot of its own
/// (Alpha Dana, Beta Dana), booked on a day long past. Bookings are made through the engine itself, as a donor,
/// because the donor's endpoint is plan 03's next slice (5.5).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class InstitutionBookingEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "institution-booking-test-signing-key-at-least-32-bytes";

    /// <summary>In the schema, but cut from plan 03: no slot may offer it.</summary>
    private static readonly string[] CatererOnly = ["Caterer"];

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;
    private Guid _alphaManager;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    /// <summary>Ten days ahead in Colombo: inside every slot's advance window here, and not yet closable.</summary>
    private static DateOnly SlotDay => SriLankaTime.Today(DateTime.UtcNow).AddDays(10);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @beta, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m WHERE m.Code = 'BOOKINGS';

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
        _alphaManager = await MemberAsync(database.AlphaInstitutionId, "MANAGER");
        _alphaStaff = await MemberAsync(database.AlphaInstitutionId, "STAFF");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public async Task DisposeAsync()
    {
        // The reseed clears the slots, blocks, bookings and roles these users made or hold, which
        // would otherwise keep them from being deleted.
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
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
            VALUES (@id, @userType, N'Calendar Test', @phone, 1, 'en', 'Active', 'stamp');
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

    private HttpClient ClientFor(Guid userId, int? institutionId)
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
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await BodyOf(response)).GetProperty("code").GetString());
    }

    private static object SlotBody(
        string name,
        string mode = "Exclusive",
        byte? maxSponsors = null,
        short maxHeadcount = 50,
        bool requiresApproval = true,
        string? version = null) => new
        {
            name,
            nameSi = name + " si",
            startTime = "11:00",
            endTime = "12:30",
            daysOfWeekMask = 127,
            bookingMode = mode,
            maxSponsors,
            minHeadcount = 5,
            maxHeadcount,
            estimatedCostPerHead = 450.50m,
            minAdvanceDays = 1,
            maxAdvanceDays = 90,
            requiresApproval,
            allowedFulfilment = new[] { "InstitutionPrepares", "DonorBrings" },
            sortOrder = 2,
            version,
        };

    /// <summary>A slot made through the API by the institution's owner; its public id and version.</summary>
    private static async Task<(Guid PublicId, string Version)> CreateSlotAsync(
        HttpClient owner,
        string? name = null,
        string mode = "Exclusive",
        byte? maxSponsors = null)
    {
        using var response = await SendAsync(owner, HttpMethod.Post, "/api/institution/meal-slots",
            SlotBody(name ?? "Dana " + Guid.NewGuid().ToString("N")[..8], mode, maxSponsors));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await BodyOf(response);
        return (body.GetProperty("publicId").GetGuid(), body.GetProperty("version").GetString()!);
    }

    private SethikaDbContext ContextFor(Guid userId) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.ForUser(userId)))
            .Options);

    /// <summary>A booking by <paramref name="donor"/>, through the engine.</summary>
    private async Task<BookMealResult> BookAsync(Guid donor, Guid slot, DateOnly? date = null, short headcount = 10)
    {
        await using var context = ContextFor(donor);
        var handler = new BookMealHandler(context, new NoLocks(), new SqlSlotInventory(context), new SqlNumberSeries(context), TimeProvider.System);
        return await handler.BookAsync(donor, new BookMealCommand(slot, date ?? SlotDay, headcount, FulfilmentMode.DonorBrings, null, null));
    }

    private async Task<string> BookedAsync(Guid slot, DateOnly? date = null, short headcount = 10)
    {
        var result = await BookAsync(await UserAsync("Donor"), slot, date, headcount);
        Assert.True(result.Succeeded, result.Error);
        return result.BookingNo!;
    }

    private sealed class NoLocks : ISlotLocks
    {
        public Task<ILockLease> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ILockLease>(new Unlocked());

        private sealed class Unlocked : ILockLease
        {
            public LockOutcome Outcome => LockOutcome.Unavailable;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed record DayCounters(byte BookedSponsors, short BookedHeadcount, string Status);

    private async Task<DayCounters> CountersAsync(Guid slot, DateOnly? date = null)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<DayCounters>(
            """
            SELECT i.BookedSponsors, i.BookedHeadcount, i.Status
            FROM booking.SlotInstances i JOIN booking.MealSlots s ON s.Id = i.MealSlotId
            WHERE s.PublicId = @slot AND i.SlotDate = @date;
            """,
            new { slot, date = (date ?? SlotDay).ToDateTime(TimeOnly.MinValue) });
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string CalendarPath(string? status = null) =>
        $"/api/institution/bookings?from={Iso(SlotDay.AddDays(-5))}&to={Iso(SlotDay.AddDays(5))}" + (status is null ? "" : "&status=" + status);

    private static string[] BookingNos(JsonElement days) =>
        [.. days.EnumerateArray().SelectMany(d => d.GetProperty("bookings").EnumerateArray()).Select(b => b.GetProperty("bookingNo").GetString()!)];

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    // ------------------------------------------------------------------ the sidebar's counts (plan 05, R3)

    [Fact]
    public async Task TheSidebarCountsRequestsStillToAnswer_OnlyForThoseWhoMayAnswerThem()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var (betaSlot, _) = await CreateSlotAsync(Beta(_betaOwner));

        await BookedAsync(slot, SlotDay);                    // counted
        var decided = await BookedAsync(slot, SlotDay.AddDays(1));
        var past = await BookedAsync(slot, SlotDay.AddDays(2));
        await BookedAsync(betaSlot, SlotDay);                // another institution's

        await using (var connection = await PlatformConnectionAsync())
        {
            // One already answered, and one whose day has gone (its slot-day moved into the past).
            await connection.ExecuteAsync(
                """
                UPDATE booking.Bookings SET Status = 'Approved', DecidedAt = SYSUTCDATETIME(), DecidedBy = @owner WHERE BookingNo = @decided;
                UPDATE si SET SlotDate = DATEADD(DAY, -30, si.SlotDate)
                FROM booking.SlotInstances si JOIN booking.Bookings b ON b.SlotInstanceId = si.Id WHERE b.BookingNo = @past;
                """,
                new { owner = _alphaOwner, decided, past });
        }

        using var forOwner = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/me/summary");
        using var forStaff = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/me/summary");
        using var forBeta = await SendAsync(Beta(_betaOwner), HttpMethod.Get, "/api/me/summary");

        Assert.Equal(HttpStatusCode.OK, forOwner.StatusCode);
        var ownerSummary = await BodyOf(forOwner);
        Assert.Equal(1, ownerSummary.GetProperty("bookingsToAnswer").GetInt32());
        Assert.Equal(JsonValueKind.Null, ownerSummary.GetProperty("registrationsWaiting").ValueKind);
        Assert.Equal(0, ownerSummary.GetProperty("invitations").GetInt32());

        // Staff see the calendar but may not approve: no count, not a zero.
        Assert.Equal(JsonValueKind.Null, (await BodyOf(forStaff)).GetProperty("bookingsToAnswer").ValueKind);
        Assert.Equal(1, (await BodyOf(forBeta)).GetProperty("bookingsToAnswer").GetInt32());
    }

    [Fact]
    public async Task TodaysTableLaysEveryMealOfTheDay_BookedOrNot_AndOnlyTheInstitutionsOwn()
    {
        var today = SriLankaTime.Today(DateTime.UtcNow);
        var (full, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table full");
        var (shared, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table shared", "Shared", 4);
        var (empty, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table empty");
        var (blocked, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table blocked");
        var (offDay, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table off day");
        var (outOfSeason, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table out of season");
        var (inactive, _) = await CreateSlotAsync(Alpha(_alphaOwner), "Table inactive");
        var (betaSlot, _) = await CreateSlotAsync(Beta(_betaOwner), "Table beta");

        await BookedAsync(full, SlotDay);
        await BookedAsync(shared, SlotDay);
        var approved = await BookedAsync(shared, SlotDay);
        await BookedAsync(betaSlot, SlotDay);

        await using (var connection = await PlatformConnectionAsync())
        {
            // Booked ten days ahead, then moved to today: nobody may book a slot for today here.
            await connection.ExecuteAsync(
                """
                UPDATE booking.Bookings SET Status = 'Approved', DecidedAt = SYSUTCDATETIME(), DecidedBy = @owner WHERE BookingNo = @approved;
                UPDATE booking.SlotInstances SET SlotDate = @today WHERE SlotDate = @slotDay;
                UPDATE booking.MealSlots SET StartTime = '07:00', MaxSponsors = 6 WHERE PublicId = @shared; -- the slot-day keeps its 4 (C3)
                UPDATE booking.MealSlots SET DaysOfWeekMask = 127 - @todayBit WHERE PublicId = @offDay;
                UPDATE booking.MealSlots SET IsActive = 0 WHERE PublicId = @inactive;
                INSERT booking.MealSlotSeasons (MealSlotId, Name, DateFrom, DateTo)
                SELECT s.Id, N'Next season', DATEADD(DAY, 30, @today), DATEADD(DAY, 60, @today) FROM booking.MealSlots s WHERE s.PublicId = @outOfSeason;
                INSERT booking.BlockedDates (InstitutionId, MealSlotId, DateFrom, DateTo, Reason, CreatedBy)
                SELECT s.InstitutionId, s.Id, @today, @today, N'Festival', @owner FROM booking.MealSlots s WHERE s.PublicId = @blocked;
                """,
                new
                {
                    owner = _alphaOwner,
                    approved,
                    today = today.ToDateTime(TimeOnly.MinValue),
                    slotDay = SlotDay.ToDateTime(TimeOnly.MinValue),
                    shared,
                    offDay,
                    todayBit = (int)MealSlot.DayBit(today.DayOfWeek),
                    inactive,
                    outOfSeason,
                    blocked,
                });
        }

        using var forStaff = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/today");
        Assert.Equal(HttpStatusCode.OK, forStaff.StatusCode);
        var table = await BodyOf(forStaff);
        Assert.Equal(Iso(today), table.GetProperty("date").GetString());

        // The seed's own slots run every day too; only this test's are compared, and the ones that
        // must not be laid are among them.
        HashSet<Guid> made = [full, shared, empty, blocked, offDay, outOfSeason, inactive, betaSlot];
        var slots = table.GetProperty("slots").EnumerateArray().Where(s => made.Contains(s.GetProperty("mealSlot").GetGuid())).ToList();
        Assert.Equal([shared, blocked, empty, full], slots.Select(s => s.GetProperty("mealSlot").GetGuid()));

        static string Line(JsonElement s) =>
            $"{s.GetProperty("name").GetString()} {s.GetProperty("startTime").GetString()} " +
            $"{s.GetProperty("bookedSponsors").GetInt32()}/{s.GetProperty("maxSponsors").GetInt32()} " +
            $"requested {s.GetProperty("requested").GetInt32()} {s.GetProperty("state").GetString()}";

        Assert.Equal(
            [
                "Table shared 07:00 2/4 requested 1 Open",
                "Table blocked 11:00 0/1 requested 0 Closed",
                "Table empty 11:00 0/1 requested 0 Open",
                "Table full 11:00 1/1 requested 1 Full",
            ],
            slots.Select(Line));

        using var forBeta = await SendAsync(Beta(_betaOwner), HttpMethod.Get, "/api/institution/today");
        Assert.Equal([betaSlot], (await BodyOf(forBeta)).GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("mealSlot").GetGuid()).Where(made.Contains));
    }

    [Fact]
    public async Task TodaysTableIsTheCalendarsToRead()
    {
        // A member holding no role in Alpha: no calendar, no table.
        var nobody = await UserAsync("InstitutionStaff");

        using var refused = await SendAsync(Alpha(nobody), HttpMethod.Get, "/api/institution/today");
        using var anonymous = await SendAsync(_host.CreateClient(), HttpMethod.Get, "/api/institution/today");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // ------------------------------------------------------------------ meal slots

    [Fact]
    public async Task ASlotIsCreatedReadAndEditedAtTheVersionTheEditorRead()
    {
        var (slot, version) = await CreateSlotAsync(Alpha(_alphaOwner), "Morning dana", "Shared", 4);

        using (var list = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/meal-slots"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var read = Assert.Single((await BodyOf(list)).EnumerateArray(), s => s.GetProperty("name").GetString() != "Alpha Dana");

            Assert.Equal(slot, read.GetProperty("publicId").GetGuid());
            Assert.Equal("Morning dana", read.GetProperty("name").GetString());
            Assert.Equal("Morning dana si", read.GetProperty("nameSi").GetString());
            Assert.Equal(("11:00", "12:30"), (read.GetProperty("startTime").GetString(), read.GetProperty("endTime").GetString()));
            Assert.Equal(("Shared", 4), (read.GetProperty("bookingMode").GetString(), read.GetProperty("maxSponsors").GetInt32()));
            Assert.Equal((5, 50), (read.GetProperty("minHeadcount").GetInt32(), read.GetProperty("maxHeadcount").GetInt32()));
            Assert.Equal(450.50m, read.GetProperty("estimatedCostPerHead").GetDecimal());

            // Stored in the order offered, whatever order the form sent.
            Assert.Equal(["DonorBrings", "InstitutionPrepares"], Strings(read.GetProperty("allowedFulfilment")));
            Assert.True(read.GetProperty("isActive").GetBoolean());
            Assert.Equal(version, read.GetProperty("version").GetString());

            // Never the internal id (CLAUDE.md).
            Assert.False(read.TryGetProperty("id", out _));
        }

        string newVersion;
        using (var edit = await SendAsync(Alpha(_alphaManager), HttpMethod.Put, $"/api/institution/meal-slots/{slot}",
                   SlotBody("Morning dana", "Exclusive", maxHeadcount: 80, version: version)))
        {
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            newVersion = (await BodyOf(edit)).GetProperty("version").GetString()!;
            Assert.NotEqual(version, newVersion);
        }

        // A second editor who read the first version loses, and changes nothing.
        using (var stale = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/meal-slots/{slot}",
                   SlotBody("Evening dana", "Exclusive", maxHeadcount: 20, version: version)))
        {
            await AssertRefusedAsync(stale, HttpStatusCode.Conflict, "stale");
        }

        using var after = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/meal-slots");
        var saved = Assert.Single((await BodyOf(after)).EnumerateArray(), s => s.GetProperty("publicId").GetGuid() == slot);
        Assert.Equal(("Morning dana", "Exclusive", 1, 80), (
            saved.GetProperty("name").GetString(),
            saved.GetProperty("bookingMode").GetString(),
            saved.GetProperty("maxSponsors").GetInt32(),
            saved.GetProperty("maxHeadcount").GetInt32()));
        Assert.Equal(newVersion, saved.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AnEditWithoutTheVersionItStartedFromIsRefused()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));

        using var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/meal-slots/{slot}", SlotBody("Lunch"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["version"], Strings((await BodyOf(response)).GetProperty("fields")));
    }

    [Fact]
    public async Task AWrongSlotNamesEveryFieldToFix()
    {
        using var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/meal-slots", new
        {
            name = " ",
            startTime = "12:00",
            endTime = "11:00",
            daysOfWeekMask = 0,
            bookingMode = "Shared",
            maxSponsors = 1,
            minHeadcount = 10,
            maxHeadcount = 5,
            estimatedCostPerHead = 10.005m,
            minAdvanceDays = 5,
            maxAdvanceDays = 2,
            allowedFulfilment = CatererOnly,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(
            ["name", "endTime", "daysOfWeekMask", "maxSponsors", "maxHeadcount", "estimatedCostPerHead", "maxAdvanceDays", "allowedFulfilment"],
            Strings(body.GetProperty("fields")));
    }

    [Fact]
    public async Task ASlotNameIsTheInstitutionsOwn()
    {
        await CreateSlotAsync(Alpha(_alphaOwner), "Lunch");

        using (var again = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/meal-slots", SlotBody("Lunch")))
        {
            await AssertRefusedAsync(again, HttpStatusCode.Conflict, "slot_name_taken");
        }

        await CreateSlotAsync(Beta(_betaOwner), "Lunch");
    }

    [Fact]
    public async Task ADeactivatedSlotTakesNoBookings_KeepsItsOwn_AndNamesThem()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var booked = await BookedAsync(slot);

        using (var off = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate"))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            Assert.Equal([booked], Strings((await BodyOf(off)).GetProperty("bookings")));
        }

        var donor = await UserAsync("Donor");
        Assert.Equal(BookingErrors.SlotInactive, (await BookAsync(donor, slot, SlotDay.AddDays(1))).Error);

        using (var on = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/activate"))
        {
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        }

        Assert.True((await BookAsync(donor, slot, SlotDay.AddDays(1))).Succeeded);
    }

    // ------------------------------------------------------------------ who may do what

    [Fact]
    public async Task StaffReadButChangeNothing()
    {
        var (slot, version) = await CreateSlotAsync(Alpha(_alphaOwner));
        var booked = await BookedAsync(slot);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/meal-slots")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/blocked-dates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/listing")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, CalendarPath())).StatusCode);

        (HttpMethod Method, string Path, object? Body)[] writes =
        [
            (HttpMethod.Post, "/api/institution/meal-slots", SlotBody("Staff slot")),
            (HttpMethod.Put, $"/api/institution/meal-slots/{slot}", SlotBody("Staff edit", version: version)),
            (HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate", null),
            (HttpMethod.Post, $"/api/institution/meal-slots/{slot}/activate", null),
            (HttpMethod.Post, "/api/institution/blocked-dates", new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay), reason = "Staff" }),
            (HttpMethod.Delete, "/api/institution/blocked-dates/1", null),
            (HttpMethod.Put, "/api/institution/listing", new { listed = true }),
            (HttpMethod.Post, $"/api/institution/bookings/{booked}/approve", null),
            (HttpMethod.Post, $"/api/institution/bookings/{booked}/reject", new { reason = "Staff" }),
            (HttpMethod.Post, $"/api/institution/bookings/{booked}/cancel", new { reason = "Staff" }),
            (HttpMethod.Post, $"/api/institution/bookings/{booked}/complete", null),
            (HttpMethod.Post, $"/api/institution/bookings/{booked}/no-show", null),
        ];

        foreach (var (method, path, body) in writes)
        {
            using var response = await SendAsync(Alpha(_alphaStaff), method, path, body);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} answered {response.StatusCode}");
        }

        using var calendar = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, CalendarPath());
        Assert.Equal("Requested", (await BodyOf(calendar))[0].GetProperty("bookings")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task NothingIsReachedWithoutSigningIn()
    {
        using var client = _host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/institution/meal-slots")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CalendarPath())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/institution/bookings/BK-ALPHA/approve", null)).StatusCode);
    }

    // ------------------------------------------------------------------ one institution, never another

    [Fact]
    public async Task AnotherInstitutionsSlotsAndBlocksAreNotFound()
    {
        var (slot, version) = await CreateSlotAsync(Alpha(_alphaOwner), "Alpha lunch");
        int block;
        using (var added = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
                   new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay), reason = "Festival" }))
        {
            block = (await BodyOf(added)).GetProperty("id").GetInt32();
        }

        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Put, $"/api/institution/meal-slots/{slot}", SlotBody("Taken over", version: version)),
            HttpStatusCode.NotFound, "slot_not_found");
        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate"),
            HttpStatusCode.NotFound, "slot_not_found");
        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
                new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay), mealSlot = slot, reason = "Not ours" }),
            HttpStatusCode.NotFound, "slot_not_found");
        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Delete, $"/api/institution/blocked-dates/{block}"),
            HttpStatusCode.NotFound, "blocked_date_not_found");

        using (var betaSlots = await SendAsync(Beta(_betaOwner), HttpMethod.Get, "/api/institution/meal-slots"))
        {
            Assert.Equal(["Beta Dana"], (await BodyOf(betaSlots)).EnumerateArray().Select(s => s.GetProperty("name").GetString()!));
        }

        using (var betaBlocks = await SendAsync(Beta(_betaOwner), HttpMethod.Get, "/api/institution/blocked-dates"))
        {
            Assert.Empty((await BodyOf(betaBlocks)).EnumerateArray());
        }

        // Alpha's are as they were.
        using var alphaSlots = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/meal-slots");
        var alpha = Assert.Single((await BodyOf(alphaSlots)).EnumerateArray(), s => s.GetProperty("publicId").GetGuid() == slot);
        Assert.Equal(("Alpha lunch", true), (alpha.GetProperty("name").GetString(), alpha.GetProperty("isActive").GetBoolean()));

        using var alphaBlocks = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/blocked-dates");
        Assert.Equal(block, Assert.Single((await BodyOf(alphaBlocks)).EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task TheCalendarHoldsOnlyTheInstitutionsOwnBookings()
    {
        var (alphaSlot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var (betaSlot, _) = await CreateSlotAsync(Beta(_betaOwner));

        var alphaBooking = await BookedAsync(alphaSlot);
        await BookedAsync(betaSlot);

        // Alpha's owner has also booked a meal at Beta, as a donor. Row-Level Security shows it to
        // them, through its owner branch; Alpha's calendar must not (§8, E8).
        var ownBetaBooking = await BookAsync(_alphaOwner, betaSlot, SlotDay.AddDays(1));
        Assert.True(ownBetaBooking.Succeeded, ownBetaBooking.Error);

        using (var calendar = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, CalendarPath()))
        {
            Assert.Equal(HttpStatusCode.OK, calendar.StatusCode);
            var days = await BodyOf(calendar);

            Assert.Equal([alphaBooking], BookingNos(days));
            var day = Assert.Single(days.EnumerateArray());
            Assert.Equal(alphaSlot, day.GetProperty("mealSlot").GetGuid());
            Assert.Equal(Iso(SlotDay), day.GetProperty("date").GetString());
            Assert.Equal((1, 1, 10), (day.GetProperty("bookedSponsors").GetInt32(), day.GetProperty("maxSponsors").GetInt32(), day.GetProperty("bookedHeadcount").GetInt32()));
        }

        // Nor may it decide on it.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{ownBetaBooking.BookingNo}/approve"),
            HttpStatusCode.NotFound, "booking_not_found");

        using var beta = await SendAsync(Beta(_betaOwner), HttpMethod.Get, CalendarPath());
        Assert.DoesNotContain(alphaBooking, BookingNos(await BodyOf(beta)));
    }

    [Fact]
    public async Task TheCalendarIsReadOneRangeAndOneStatusAtATime()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner), mode: "Shared", maxSponsors: 3);
        var requested = await BookedAsync(slot);
        var approved = await BookedAsync(slot);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{approved}/approve")).StatusCode);

        using (var waiting = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, CalendarPath("Requested")))
        {
            Assert.Equal([requested], BookingNos(await BodyOf(waiting)));
        }

        using (var none = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, CalendarPath("Completed")))
        {
            Assert.Empty((await BodyOf(none)).EnumerateArray());
        }

        var from = SlotDay;
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, CalendarPath("Waiting")), HttpStatusCode.BadRequest, "invalid_status");
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"/api/institution/bookings?from={Iso(from)}"), HttpStatusCode.BadRequest, "invalid_range");
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"/api/institution/bookings?from={Iso(from)}&to={Iso(from.AddDays(-1))}"),
            HttpStatusCode.BadRequest, "invalid_range");
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"/api/institution/bookings?from={Iso(from)}&to={Iso(from.AddDays(62))}"),
            HttpStatusCode.BadRequest, "invalid_range");
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, $"/api/institution/bookings?from={Iso(from)}&to={Iso(from.AddDays(61))}")).StatusCode);
    }

    // ------------------------------------------------------------------ decisions

    [Fact]
    public async Task ARejectionSaysWhy_AndGivesThePlaceBack()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var booked = await BookedAsync(slot);
        Assert.Equal(new DayCounters(1, 10, "Full"), await CountersAsync(slot));

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaManager), HttpMethod.Post, $"/api/institution/bookings/{booked}/reject"),
            HttpStatusCode.BadRequest, "reason_required");
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaManager), HttpMethod.Post, $"/api/institution/bookings/{booked}/reject", new { reason = new string('x', 301) }),
            HttpStatusCode.BadRequest, "reason_too_long");
        Assert.Equal(new DayCounters(1, 10, "Full"), await CountersAsync(slot));

        using (var rejected = await SendAsync(Alpha(_alphaManager), HttpMethod.Post, $"/api/institution/bookings/{booked}/reject", new { reason = "Closed for a retreat" }))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.Equal("Rejected", (await BodyOf(rejected)).GetProperty("status").GetString());
        }

        Assert.Equal(new DayCounters(0, 0, "Open"), await CountersAsync(slot));

        using var calendar = await SendAsync(Alpha(_alphaManager), HttpMethod.Get, CalendarPath());
        var booking = (await BodyOf(calendar))[0].GetProperty("bookings")[0];
        Assert.Equal(("Rejected", "Closed for a retreat"), (booking.GetProperty("status").GetString(), booking.GetProperty("rejectReason").GetString()));

        // The day is free for the next donor.
        await BookedAsync(slot);
    }

    [Fact]
    public async Task AnApprovedBookingIsCancelledWithAReason_AndGivesThePlaceBack()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var booked = await BookedAsync(slot);

        using (var approved = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/approve"))
        {
            Assert.Equal("Approved", (await BodyOf(approved)).GetProperty("status").GetString());
        }

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/approve"),
            HttpStatusCode.Conflict, "invalid_transition");

        // Ten days early: the meal has not happened, so it can be neither completed nor missed.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/complete"),
            HttpStatusCode.Conflict, "too_early");
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/no-show"),
            HttpStatusCode.Conflict, "too_early");

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/cancel"),
            HttpStatusCode.BadRequest, "reason_required");
        Assert.Equal(new DayCounters(1, 10, "Full"), await CountersAsync(slot));

        using (var cancelled = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{booked}/cancel", new { reason = "The kitchen is closed" }))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal("Cancelled", (await BodyOf(cancelled)).GetProperty("status").GetString());
        }

        Assert.Equal(new DayCounters(0, 0, "Open"), await CountersAsync(slot));
    }

    [Fact]
    public async Task AMealIsCompletedOrMissedFromItsDay()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner), mode: "Shared", maxSponsors: 2);
        var eaten = await BookedAsync(slot);
        var missed = await BookedAsync(slot);

        foreach (var no in new[] { eaten, missed })
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{no}/approve")).StatusCode);
        }

        // As if the day had come: the slot-day moved to today.
        await ExecuteAsync(
            "UPDATE i SET SlotDate = @today FROM booking.SlotInstances i JOIN booking.MealSlots s ON s.Id = i.MealSlotId WHERE s.PublicId = @slot;",
            new { slot, today = SriLankaTime.Today(DateTime.UtcNow).ToDateTime(TimeOnly.MinValue) });

        using (var completed = await SendAsync(Alpha(_alphaManager), HttpMethod.Post, $"/api/institution/bookings/{eaten}/complete"))
        {
            Assert.Equal("Completed", (await BodyOf(completed)).GetProperty("status").GetString());
        }

        using (var noShow = await SendAsync(Alpha(_alphaManager), HttpMethod.Post, $"/api/institution/bookings/{missed}/no-show"))
        {
            Assert.Equal("NoShow", (await BodyOf(noShow)).GetProperty("status").GetString());
        }

        // Both keep their places (C1): the meal was promised, and taken.
        Assert.Equal(new DayCounters(2, 20, "Full"), await CountersAsync(slot, SriLankaTime.Today(DateTime.UtcNow)));
    }

    [Fact]
    public async Task AnUnknownBookingIsNotFound() =>
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/bookings/BK-2026-999999/approve"),
            HttpStatusCode.NotFound, "booking_not_found");

    // ------------------------------------------------------------------ blocked dates

    [Fact]
    public async Task ABlockOverBookingsNamesTheActiveOnes_AndTakesTheDaysOut()
    {
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner), mode: "Shared", maxSponsors: 3);
        var (otherSlot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var active = await BookedAsync(slot);
        var cancelled = await BookedAsync(slot);
        await BookedAsync(slot, SlotDay.AddDays(3));
        await BookedAsync(otherSlot);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/bookings/{cancelled}/cancel", new { reason = "Asked to" })).StatusCode);

        int id;
        using (var added = await SendAsync(Alpha(_alphaManager), HttpMethod.Post, "/api/institution/blocked-dates",
                   new { dateFrom = Iso(SlotDay.AddDays(-1)), dateTo = Iso(SlotDay.AddDays(1)), mealSlot = slot, reason = "Kitchen renovation" }))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var body = await BodyOf(added);
            id = body.GetProperty("id").GetInt32();

            // Only the slot's active bookings inside the range: not the cancelled one, not a later day,
            // not another slot's.
            Assert.Equal([active], Strings(body.GetProperty("bookings")));
        }

        using (var list = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/blocked-dates"))
        {
            var block = Assert.Single((await BodyOf(list)).EnumerateArray());
            Assert.Equal(id, block.GetProperty("id").GetInt32());
            Assert.Equal(slot, block.GetProperty("mealSlot").GetGuid());
            Assert.Equal("Kitchen renovation", block.GetProperty("reason").GetString());
        }

        var donor = await UserAsync("Donor");
        Assert.Equal(BookingErrors.DateBlocked, (await BookAsync(donor, slot, SlotDay.AddDays(1))).Error);
        Assert.NotEqual(BookingErrors.DateBlocked, (await BookAsync(donor, otherSlot, SlotDay.AddDays(1))).Error);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(_alphaManager), HttpMethod.Delete, $"/api/institution/blocked-dates/{id}")).StatusCode);
        Assert.True((await BookAsync(donor, slot, SlotDay.AddDays(1))).Succeeded);

        using var empty = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/blocked-dates");
        Assert.Empty((await BodyOf(empty)).EnumerateArray());
    }

    [Fact]
    public async Task ABlockForEverySlotNamesEverySlotsBookings()
    {
        var (first, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var (second, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        var one = await BookedAsync(first);
        var two = await BookedAsync(second);

        using var added = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay), reason = "Closed" });

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(new[] { one, two }.Order(StringComparer.Ordinal), Strings((await BodyOf(added)).GetProperty("bookings")));
    }

    [Fact]
    public async Task AWrongBlockNamesEveryFieldToFix()
    {
        using var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay.AddDays(-1)), reason = "" });
        Assert.Equal(["dateTo", "reason"], Strings((await BodyOf(response)).GetProperty("fields")));

        var yesterday = SriLankaTime.Today(DateTime.UtcNow).AddDays(-1);
        using var past = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(yesterday.AddDays(-3)), dateTo = Iso(yesterday), reason = "Over" });
        Assert.Equal(["dateTo"], Strings((await BodyOf(past)).GetProperty("fields")));

        using var tooLong = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(SlotDay), dateTo = Iso(SlotDay.AddDays(366)), reason = "A year and a day" });
        Assert.Equal(["dateTo"], Strings((await BodyOf(tooLong)).GetProperty("fields")));
    }

    // ------------------------------------------------------------------ the public list

    [Fact]
    public async Task AnInstitutionIsListedOnlyWithASlotToBook()
    {
        await ExecuteAsync("UPDATE booking.MealSlots SET IsActive = 0 WHERE InstitutionId = @alpha;", new { alpha = database.AlphaInstitutionId });

        using (var start = await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/listing"))
        {
            var body = await BodyOf(start);
            Assert.Equal(("alpha-temple", false), (body.GetProperty("slug").GetString(), body.GetProperty("listed").GetBoolean()));
        }

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, "/api/institution/listing", new { listed = true }),
            HttpStatusCode.Conflict, "no_active_slot");

        // An inactive slot is not one to book.
        var (slot, _) = await CreateSlotAsync(Alpha(_alphaOwner));
        await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate");
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, "/api/institution/listing", new { listed = true }),
            HttpStatusCode.Conflict, "no_active_slot");

        await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/activate");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, "/api/institution/listing", new { listed = true })).StatusCode);

        using (var listed = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/listing"))
        {
            Assert.True((await BodyOf(listed)).GetProperty("listed").GetBoolean());
        }

        // Hiding needs no slot, and Beta is untouched.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, "/api/institution/listing", new { listed = false })).StatusCode);

        await using var connection = await PlatformConnectionAsync();
        var flags = (await connection.QueryAsync<(string, bool)>(
            "SELECT InstitutionCode, IsPubliclyListed FROM org.Institutions WHERE InstitutionCode IN ('ALPHA', 'BETA') ORDER BY InstitutionCode;")).ToList();
        Assert.Equal(new[] { ("ALPHA", false), ("BETA", false) }, flags);
    }

    [Fact]
    public async Task TheListingSwitchSaysWhatIsMissing()
    {
        using var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, "/api/institution/listing", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["listed"], Strings((await BodyOf(response)).GetProperty("fields")));
    }
}

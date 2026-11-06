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
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Caching;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// The donor's booking API over HTTP (plan 03, 5.5), against real SQL Server and Redis: the
/// anonymous institution reads and what they leave out, the availability month, every refusal of
/// the engine as the donor receives it, a donor's own bookings only, and the cancel window (Q1).
/// </summary>
/// <remarks>
/// Slots are written straight into the tables, as the platform: how an institution makes them is
/// <c>InstitutionBookingEndpointsTests</c>' subject, and this way each test states the slot it
/// needs. Bookings are made through the endpoint under test. Anonymous requests arrive through the
/// trusted loopback proxy, each client forwarded for a random address, so the per-address read limit
/// is never shared between tests or runs.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class DonorBookingEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "donor-booking-test-signing-key-at-least-32-bytes";

    /// <summary>The rule names of <c>BookingRateLimits</c>, which is internal to the Api.</summary>
    private const string PublicReadRule = "institution-read-client";

    private const string CreateRule = "booking-create-user";

    private readonly List<Guid> _users = [];
    private readonly List<string> _rateLimitKeys = [];
    private WebApplicationFactory<Program> _host = null!;

    /// <summary>
    /// The first of the month 40 days ahead. Every day of it is 10 to 71 days away: inside a 1-90 day
    /// advance window, and not yet closable, whatever today is.
    /// </summary>
    private static DateOnly Month
    {
        get
        {
            var ahead = SriLankaTime.Today(DateTime.UtcNow).AddDays(40);
            return new DateOnly(ahead.Year, ahead.Month, 1);
        }
    }

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

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
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });

        if (_rateLimitKeys.Count > 0)
        {
            await using var redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
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

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<Guid> DonorAsync()
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Booking Donor', @phone, 1, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone });

        _users.Add(id);
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

    /// <summary>A client with no token, forwarded for <paramref name="address"/> or a random one.</summary>
    private HttpClient Anonymous(string? address = null)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address ?? NewAddress());
        return client;
    }

    private static string NewAddress() =>
        $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}";

    /// <summary>A slot of <paramref name="institutionId"/>, written as the platform; its public id.</summary>
    private async Task<Guid> SlotAsync(
        int institutionId,
        string mode = "Exclusive",
        byte maxSponsors = 1,
        short minHeadcount = 5,
        short maxHeadcount = 50,
        bool requiresApproval = true,
        byte days = MealSlot.EveryDay,
        string fulfilment = "DonorBrings,InstitutionPrepares",
        bool active = true)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, DaysOfWeekMask, BookingMode, MaxSponsors,
                 MinHeadcount, MaxHeadcount, EstimatedCostPerHead, MinAdvanceDays, MaxAdvanceDays,
                 RequiresApproval, AllowedFulfilment, SortOrder, IsActive, CreatedBy)
            VALUES (@publicId, @institutionId, @name, '11:00', '12:30', @days, @mode, @maxSponsors,
                    @minHeadcount, @maxHeadcount, 450.00, 1, 90, @requiresApproval, @fulfilment, 1, @active, @createdBy);
            """,
            new
            {
                publicId,
                institutionId,
                name = "Dana " + Guid.NewGuid().ToString("N")[..8],
                days,
                mode,
                maxSponsors,
                minHeadcount,
                maxHeadcount,
                requiresApproval,
                fulfilment,
                active,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    private Task BlockAsync(int institutionId, DateOnly from, DateOnly? to = null) =>
        ExecuteAsync(
            """
            INSERT booking.BlockedDates (InstitutionId, DateFrom, DateTo, Reason, CreatedBy)
            VALUES (@institutionId, @from, @to, N'Festival', @createdBy);
            """,
            new
            {
                institutionId,
                from = from.ToDateTime(TimeOnly.MinValue),
                to = (to ?? from).ToDateTime(TimeOnly.MinValue),
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

    private Task SetInstitutionAsync(int institutionId, string status = "Active", bool listed = false) =>
        ExecuteAsync(
            "UPDATE org.Institutions SET Status = @status, IsPubliclyListed = @listed WHERE Id = @institutionId;",
            new { institutionId, status, listed });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            return await client.SendAsync(request);
        }
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient donor, Guid slot, DateOnly date, short headcount = 10, string fulfilment = "DonorBrings") =>
        SendAsync(donor, HttpMethod.Post, "/api/bookings", new { mealSlot = slot, date = Iso(date), headcount, fulfilment, inMemoryOf = "Amma", notes = "Rice and curry" });

    private async Task<string> BookedAsync(Guid donor, Guid slot, DateOnly date, short headcount = 10)
    {
        using var response = await BookAsync(ClientFor(donor), slot, date, headcount);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyOf(response)).GetProperty("bookingNo").GetString()!;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await BodyOf(response)).GetProperty("code").GetString());
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string MonthOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    private async Task<JsonElement> AvailabilityAsync(string slug, DateOnly month)
    {
        using var response = await SendAsync(Anonymous(), HttpMethod.Get, $"/api/institutions/{slug}/availability?month={MonthOf(month)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyOf(response);
    }

    /// <summary>One slot-day of an availability response.</summary>
    private static JsonElement Day(JsonElement availability, Guid slot, DateOnly date) =>
        availability.GetProperty("slots").EnumerateArray()
            .Single(s => s.GetProperty("mealSlot").GetGuid() == slot)
            .GetProperty("days").EnumerateArray()
            .Single(d => d.GetProperty("date").GetString() == Iso(date));

    private static (bool Bookable, string? Reason, int Sponsors, int Headcount) Openings(JsonElement day) => (
        day.GetProperty("bookable").GetBoolean(),
        day.GetProperty("reason").GetString(),
        day.GetProperty("sponsors").GetInt32(),
        day.GetProperty("headcount").GetInt32());

    private async Task<JsonElement> MineAsync(Guid donor)
    {
        using var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyOf(response);
    }

    private static string[] BookingNos(JsonElement mine) =>
        [.. mine.EnumerateArray().Select(b => b.GetProperty("bookingNo").GetString()!).Order(StringComparer.Ordinal)];

    // ------------------------------------------------------------------ the public institution reads

    [Fact]
    public async Task TheListShowsActiveListedInstitutionsOnly_AndNothingPrivate()
    {
        await SetInstitutionAsync(database.AlphaInstitutionId, listed: true);
        await SetInstitutionAsync(database.BetaInstitutionId, listed: false);

        using (var response = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var alpha = Assert.Single((await BodyOf(response)).EnumerateArray());

            Assert.Equal("alpha-temple", alpha.GetProperty("slug").GetString());
            Assert.Equal("Alpha Temple", alpha.GetProperty("name").GetString());
            Assert.Equal("Colombo", alpha.GetProperty("city").GetString());
            Assert.False(string.IsNullOrEmpty(alpha.GetProperty("district").GetString()));
            Assert.False(string.IsNullOrEmpty(alpha.GetProperty("typeName").GetString()));

            // Exactly these, so a column added to the query later is a decision, not an accident.
            Assert.Equal(
                ["city", "district", "districtSi", "districtTa", "name", "nameSi", "nameTa", "slug", "type", "typeName", "typeNameSi", "typeNameTa"],
                Names(alpha));
        }

        // Listed, but suspended: gone from the list.
        await SetInstitutionAsync(database.AlphaInstitutionId, "Suspended", listed: true);

        using var after = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions");
        Assert.Empty((await BodyOf(after)).EnumerateArray());
    }

    [Fact]
    public async Task AnUnlistedInstitutionIsReachableByItsSlug_WithItsActiveSlotsOnly()
    {
        var active = await SlotAsync(database.BetaInstitutionId, "Shared", maxSponsors: 4);
        var inactive = await SlotAsync(database.BetaInstitutionId, active: false);

        using var response = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions/beta-temple");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var beta = await BodyOf(response);

        Assert.Equal("Beta Temple", beta.GetProperty("name").GetString());
        Assert.Equal("2 Beta Road", beta.GetProperty("addressLine1").GetString());
        Assert.Equal(6.928100m, beta.GetProperty("latitude").GetDecimal());
        Assert.Equal(
            ["addressLine1", "addressLine2", "city", "description", "dietaryNotes", "district", "districtSi", "districtTa",
             "givesOnline", "latitude", "longitude", "name", "nameSi", "nameTa", "services", "slots", "slug", "type", "typeName", "typeNameSi", "typeNameTa"],
            Names(beta));

        var slots = beta.GetProperty("slots").EnumerateArray().ToList();
        Assert.DoesNotContain(slots, s => s.GetProperty("publicId").GetGuid() == inactive);

        // The seeded Beta Dana, and the new one.
        Assert.Equal(2, slots.Count);
        var slot = Assert.Single(slots, s => s.GetProperty("publicId").GetGuid() == active);
        Assert.Equal(("11:00", "12:30"), (slot.GetProperty("startTime").GetString(), slot.GetProperty("endTime").GetString()));
        Assert.Equal(("Shared", 4), (slot.GetProperty("bookingMode").GetString(), slot.GetProperty("maxSponsors").GetInt32()));
        Assert.Equal(["DonorBrings", "InstitutionPrepares"], slot.GetProperty("allowedFulfilment").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(
            ["allowedFulfilment", "bookingMode", "daysOfWeekMask", "endTime", "estimatedCostPerHead", "maxAdvanceDays", "maxHeadcount",
             "maxSponsors", "minAdvanceDays", "minHeadcount", "name", "nameSi", "nameTa", "publicId", "requiresApproval", "startTime"],
            Names(slot));
    }

    [Fact]
    public async Task AnInstitutionThatIsNotActiveOrDoesNotExistIsNotFound()
    {
        await SetInstitutionAsync(database.BetaInstitutionId, "Suspended");

        string[] paths =
        [
            "/api/institutions/beta-temple",
            $"/api/institutions/beta-temple/availability?month={MonthOf(Month)}",
            "/api/institutions/no-such-temple",
            $"/api/institutions/no-such-temple/availability?month={MonthOf(Month)}",
            "/api/institutions/" + new string('a', 151),
        ];

        foreach (var path in paths)
        {
            using var response = await SendAsync(Anonymous(), HttpMethod.Get, path);
            await AssertRefusedAsync(response, HttpStatusCode.NotFound, "institution_not_found");
        }

        using var alpha = await SendAsync(Anonymous(), HttpMethod.Get, "/api/institutions/alpha-temple");
        Assert.Equal(HttpStatusCode.OK, alpha.StatusCode);
    }

    // ------------------------------------------------------------------ availability

    [Fact]
    public async Task TheMonthShowsEveryDayOfEverySlot_WithWhatIsLeftAndWhyNot()
    {
        var shared = await SlotAsync(database.AlphaInstitutionId, "Shared", maxSponsors: 4, maxHeadcount: 50);
        var exclusive = await SlotAsync(database.AlphaInstitutionId);
        var mondays = await SlotAsync(database.AlphaInstitutionId, days: MealSlot.DayBit(DayOfWeek.Monday));
        var inactive = await SlotAsync(database.AlphaInstitutionId, active: false);

        var booked = Month.AddDays(5);
        var blocked = Month.AddDays(6);
        var taken = Month.AddDays(7);

        await BookedAsync(await DonorAsync(), shared, booked, headcount: 12);
        await BookedAsync(await DonorAsync(), exclusive, taken);
        await BlockAsync(database.AlphaInstitutionId, blocked);

        // Began last month and ends on the first: it still blocks the first.
        await BlockAsync(database.AlphaInstitutionId, Month.AddDays(-3), Month);

        var month = await AvailabilityAsync("alpha-temple", Month);

        Assert.Equal(MonthOf(Month), month.GetProperty("month").GetString());

        var slots = month.GetProperty("slots").EnumerateArray().ToList();
        Assert.DoesNotContain(slots, s => s.GetProperty("mealSlot").GetGuid() == inactive);
        Assert.All(slots, s => Assert.Equal(DateTime.DaysInMonth(Month.Year, Month.Month), s.GetProperty("days").GetArrayLength()));

        Assert.Equal((false, BookingErrors.DateBlocked, 4, 50), Openings(Day(month, shared, Month)));
        Assert.Equal((true, null, 4, 50), Openings(Day(month, shared, Month.AddDays(1))));
        Assert.Equal((true, null, 3, 38), Openings(Day(month, shared, booked)));
        Assert.Equal((false, BookingErrors.DateBlocked, 4, 50), Openings(Day(month, shared, blocked)));
        Assert.Equal((false, BookingErrors.SlotFull, 0, 40), Openings(Day(month, exclusive, taken)));
        Assert.Equal((true, null, 1, 50), Openings(Day(month, exclusive, booked)));

        foreach (var day in month.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("mealSlot").GetGuid() == mondays).GetProperty("days").EnumerateArray())
        {
            var date = DateOnly.ParseExact(day.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            // The day of the week is checked before the block (C4).
            var expected = date.DayOfWeek != DayOfWeek.Monday ? BookingErrors.NotOnThisDay
                : date == blocked || date == Month ? BookingErrors.DateBlocked
                : null;
            Assert.Equal(expected, day.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task DaysAlreadyGoneAreTooSoon()
    {
        var slot = await SlotAsync(database.AlphaInstitutionId);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);

        var month = await AvailabilityAsync("alpha-temple", thisMonth);

        // The first of the month is today or earlier, and a slot needs a day's notice.
        var (bookable, reason, _, _) = Openings(Day(month, slot, thisMonth));
        Assert.Equal((false, BookingErrors.TooSoon), (bookable, reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-11-01")]
    [InlineData("November")]
    public async Task AMonthIsAskedForAsYearAndMonth(string month)
    {
        using var response = await SendAsync(Anonymous(), HttpMethod.Get, $"/api/institutions/alpha-temple/availability?month={month}");
        await AssertRefusedAsync(response, HttpStatusCode.BadRequest, "invalid_month");
    }

    // ------------------------------------------------------------------ booking

    [Fact]
    public async Task ADonorBooksASlotDay_AndSeesTheBooking()
    {
        var needsApproval = await SlotAsync(database.AlphaInstitutionId);
        var confirmedAtOnce = await SlotAsync(database.AlphaInstitutionId, requiresApproval: false);
        var donor = await DonorAsync();
        var date = Month.AddDays(3);

        using (var requested = await BookAsync(ClientFor(donor), needsApproval, date, headcount: 25))
        {
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            var body = await BodyOf(requested);
            Assert.StartsWith("BK-", body.GetProperty("bookingNo").GetString(), StringComparison.Ordinal);
            Assert.Equal(BookingStatus.Requested, body.GetProperty("status").GetString());
        }

        using (var confirmed = await BookAsync(ClientFor(donor), confirmedAtOnce, date))
        {
            Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
            Assert.Equal(BookingStatus.Approved, (await BodyOf(confirmed)).GetProperty("status").GetString());
        }

        var mine = (await MineAsync(donor)).EnumerateArray().ToList();
        Assert.Equal(2, mine.Count);

        var booking = Assert.Single(mine, b => b.GetProperty("mealSlot").GetGuid() == needsApproval);
        Assert.Equal(BookingStatus.Requested, booking.GetProperty("status").GetString());
        Assert.Equal(Iso(date), booking.GetProperty("date").GetString());
        Assert.Equal(("11:00", "12:30"), (booking.GetProperty("startTime").GetString(), booking.GetProperty("endTime").GetString()));
        Assert.Equal(("alpha-temple", "Alpha Temple"), (booking.GetProperty("institution").GetString(), booking.GetProperty("institutionName").GetString()));
        Assert.Equal(25, booking.GetProperty("headcount").GetInt32());
        Assert.Equal(("Amma", "Rice and curry"), (booking.GetProperty("inMemoryOf").GetString(), booking.GetProperty("notes").GetString()));
        Assert.True(booking.GetProperty("canCancel").GetBoolean());
        Assert.False(booking.TryGetProperty("donorPhone", out _));
        Assert.False(booking.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task EveryReasonTheEngineRefusesReachesTheDonor()
    {
        var slot = await SlotAsync(database.BetaInstitutionId, fulfilment: "DonorBrings");
        var inactive = await SlotAsync(database.BetaInstitutionId, active: false);
        var mondays = await SlotAsync(database.BetaInstitutionId, days: MealSlot.DayBit(DayOfWeek.Monday));
        var seasonal = await SlotAsync(database.BetaInstitutionId);
        var donor = await DonorAsync();

        var date = Month.AddDays(2);
        var notMonday = date.DayOfWeek == DayOfWeek.Monday ? date.AddDays(1) : date;
        var blocked = Month.AddDays(12);

        await BlockAsync(database.BetaInstitutionId, blocked);
        await ExecuteAsync(
            """
            INSERT booking.MealSlotSeasons (MealSlotId, Name, DateFrom, DateTo)
            SELECT s.Id, N'Vesak', @from, @from FROM booking.MealSlots s WHERE s.PublicId = @seasonal;
            """,
            new { seasonal, from = Month.AddDays(20).ToDateTime(TimeOnly.MinValue) });

        (Guid Slot, DateOnly Date, short Headcount, string Fulfilment, HttpStatusCode Status, string Code)[] refusals =
        [
            (Guid.NewGuid(), date, 10, "DonorBrings", HttpStatusCode.NotFound, "slot_not_found"),
            (inactive, date, 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.SlotInactive),
            (mondays, notMonday, 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.NotOnThisDay),
            (seasonal, date, 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.OutOfSeason),
            (slot, blocked, 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.DateBlocked),
            (slot, Today, 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.TooSoon),
            (slot, Today.AddDays(91), 10, "DonorBrings", HttpStatusCode.Conflict, BookingErrors.TooFar),
            (slot, date, 4, "DonorBrings", HttpStatusCode.BadRequest, BookingErrors.HeadcountOutOfRange),
            (slot, date, 51, "DonorBrings", HttpStatusCode.BadRequest, BookingErrors.HeadcountOutOfRange),
            (slot, date, 10, "InstitutionPrepares", HttpStatusCode.BadRequest, BookingErrors.FulfilmentNotOffered),
        ];

        foreach (var (target, day, headcount, fulfilment, status, code) in refusals)
        {
            using var response = await BookAsync(ClientFor(donor), target, day, headcount, fulfilment);
            await AssertRefusedAsync(response, status, code);
        }

        // The season's own day is open, and so was the plain slot all along.
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(ClientFor(donor), seasonal, Month.AddDays(20))).StatusCode);

        await SetInstitutionAsync(database.BetaInstitutionId, "Suspended");
        using var suspended = await BookAsync(ClientFor(donor), slot, date);
        await AssertRefusedAsync(suspended, HttpStatusCode.Conflict, BookingErrors.InstitutionNotActive);

        Assert.Single((await MineAsync(donor)).EnumerateArray());
    }

    [Fact]
    public async Task AWrongBookingNamesTheFieldsToFix()
    {
        var donor = await DonorAsync();
        var slot = await SlotAsync(database.AlphaInstitutionId);

        using (var empty = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            var body = await BodyOf(empty);
            Assert.Equal("invalid_details", body.GetProperty("code").GetString());
            Assert.Equal(["mealSlot", "date", "headcount"], body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        }

        using (var badDate = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings", new { mealSlot = slot, date = "2026-11-5", headcount = 10, fulfilment = "DonorBrings" }))
        {
            Assert.Equal(["date"], (await BodyOf(badDate)).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        }

        using var rest = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings", new
        {
            mealSlot = slot,
            date = Iso(Month.AddDays(1)),
            headcount = 10,
            inMemoryOf = new string('x', 201),
            notes = new string('x', 501),
        });

        Assert.Equal(HttpStatusCode.BadRequest, rest.StatusCode);
        Assert.Equal(["fulfilment", "inMemoryOf", "notes"], (await BodyOf(rest)).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.Empty((await MineAsync(donor)).EnumerateArray());
    }

    [Fact]
    public async Task ATakenSlotDayIsRefused_ToTheSameDonorAndToTheNext()
    {
        var slot = await SlotAsync(database.AlphaInstitutionId);
        var date = Month.AddDays(9);
        var first = await DonorAsync();

        await BookedAsync(first, slot, date);

        using (var again = await BookAsync(ClientFor(first), slot, date))
        {
            // Exclusive, so the place is gone too; the engine says which: the same donor already has it.
            await AssertRefusedAsync(again, HttpStatusCode.Conflict, BookingErrors.SlotFull);
        }

        using (var second = await BookAsync(ClientFor(await DonorAsync()), slot, date))
        {
            await AssertRefusedAsync(second, HttpStatusCode.Conflict, BookingErrors.SlotFull);
        }

        var shared = await SlotAsync(database.AlphaInstitutionId, "Shared", maxSponsors: 4);
        await BookedAsync(first, shared, date);

        using var twice = await BookAsync(ClientFor(first), shared, date);
        await AssertRefusedAsync(twice, HttpStatusCode.Conflict, BookingErrors.AlreadyBooked);

        // The refused second booking gave back the place it took for a moment (C2, rolled back).
        Assert.Equal((true, null, 3, 40), Openings(Day(await AvailabilityAsync("alpha-temple", Month), shared, date)));
    }

    // ------------------------------------------------------------------ the donor's own bookings

    [Fact]
    public async Task ADonorSeesOnlyTheirOwnBookings()
    {
        var alphaSlot = await SlotAsync(database.AlphaInstitutionId, "Shared", maxSponsors: 4);
        var betaSlot = await SlotAsync(database.BetaInstitutionId);
        var date = Month.AddDays(4);

        var donor = await DonorAsync();
        var other = await DonorAsync();
        var mine = await BookedAsync(donor, alphaSlot, date);
        var theirs = await BookedAsync(other, alphaSlot, date);

        // A steward of Alpha who also books as a donor, at Beta. In their personal session they see
        // their own booking, not Alpha's: the institution's bookings are read on its calendar.
        var steward = await DonorAsync();
        await ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            SELECT @alpha, 'Institution', t.RoleLevelId, t.Code, t.Name, 0
            FROM auth.Roles t WHERE t.IsTemplate = 1 AND t.InstitutionId IS NULL AND t.Code = 'OWNER';
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @steward, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';
            """,
            new { alpha = database.AlphaInstitutionId, steward });
        var stewards = await BookedAsync(steward, betaSlot, date);

        Assert.Equal([mine], BookingNos(await MineAsync(donor)));
        Assert.Equal([theirs], BookingNos(await MineAsync(other)));
        Assert.Equal([stewards], BookingNos(await MineAsync(steward)));
    }

    [Fact]
    public async Task TheDonorCancelsUntilTheDayBefore_AndTheDayIsFreeAgain()
    {
        var slot = await SlotAsync(database.AlphaInstitutionId);
        var date = Month.AddDays(8);
        var donor = await DonorAsync();
        var bookingNo = await BookedAsync(donor, slot, date);

        Assert.Equal(BookingErrors.SlotFull, Openings(Day(await AvailabilityAsync("alpha-temple", Month), slot, date)).Reason);

        using (var cancel = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel", new { reason = "Travelling" }))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Assert.Equal(BookingStatus.Cancelled, (await BodyOf(cancel)).GetProperty("status").GetString());
        }

        Assert.Equal((true, null, 1, 50), Openings(Day(await AvailabilityAsync("alpha-temple", Month), slot, date)));

        var cancelled = Assert.Single((await MineAsync(donor)).EnumerateArray());
        Assert.Equal((BookingStatus.Cancelled, "Travelling"), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancelReason").GetString()));
        Assert.False(cancelled.GetProperty("canCancel").GetBoolean());

        using (var twice = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel"))
        {
            await AssertRefusedAsync(twice, HttpStatusCode.Conflict, BookingErrors.InvalidTransition);
        }

        // Someone else takes the freed day.
        await BookedAsync(await DonorAsync(), slot, date);

        // A booking whose day has come: moved to today, as time would move it.
        var late = await BookedAsync(donor, slot, date.AddDays(1));
        await ExecuteAsync(
            """
            UPDATE booking.SlotInstances SET SlotDate = @today
            WHERE Id = (SELECT b.SlotInstanceId FROM booking.Bookings b WHERE b.BookingNo = @late);
            """,
            new { today = Today.ToDateTime(TimeOnly.MinValue), late });

        Assert.False(Assert.Single((await MineAsync(donor)).EnumerateArray(), b => b.GetProperty("bookingNo").GetString() == late).GetProperty("canCancel").GetBoolean());

        using var tooLate = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{late}/cancel");
        await AssertRefusedAsync(tooLate, HttpStatusCode.Conflict, BookingErrors.CancelWindowClosed);
    }

    [Fact]
    public async Task NobodyCancelsSomeoneElsesBooking()
    {
        var slot = await SlotAsync(database.AlphaInstitutionId);
        var donor = await DonorAsync();
        var bookingNo = await BookedAsync(donor, slot, Month.AddDays(10));

        using (var stranger = await SendAsync(ClientFor(await DonorAsync()), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel"))
        {
            await AssertRefusedAsync(stranger, HttpStatusCode.NotFound, "booking_not_found");
        }

        using (var unknown = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings/BK-1999-000001/cancel"))
        {
            await AssertRefusedAsync(unknown, HttpStatusCode.NotFound, "booking_not_found");
        }

        using (var tooLong = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel", new { reason = new string('x', Booking.MaxReason + 1) }))
        {
            await AssertRefusedAsync(tooLong, HttpStatusCode.BadRequest, BookingErrors.ReasonTooLong);
        }

        Assert.Equal(BookingStatus.Requested, Assert.Single((await MineAsync(donor)).EnumerateArray()).GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------ who may, and how often

    [Fact]
    public async Task BookingIsDoneSignedInAndInAPersonalSession()
    {
        var slot = await SlotAsync(database.AlphaInstitutionId);
        var donor = await DonorAsync();
        var bookingNo = await BookedAsync(donor, slot, Month.AddDays(11));

        (HttpMethod Method, string Path, object? Body)[] endpoints =
        [
            (HttpMethod.Post, "/api/bookings", new { mealSlot = slot, date = Iso(Month.AddDays(12)), headcount = 10, fulfilment = "DonorBrings" }),
            (HttpMethod.Get, "/api/bookings/mine", null),
            (HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel", null),
        ];

        foreach (var (method, path, body) in endpoints)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), method, path, body)).StatusCode);

            // The DONOR role is global: a session switched into an institution does not hold it (plan 02, B1).
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(ClientFor(donor, database.AlphaInstitutionId), method, path, body)).StatusCode);
        }

        Assert.Equal(BookingStatus.Requested, Assert.Single((await MineAsync(donor)).EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ThePublicReadsAndBookingAreRateLimited()
    {
        var address = NewAddress();
        var donor = await DonorAsync();
        var readKey = RateLimitKey.For(PublicReadRule, address);
        var createKey = RateLimitKey.For(CreateRule, donor.ToString());
        _rateLimitKeys.AddRange([readKey, createKey]);

        // Each budget already spent this window.
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString))
        {
            await redis.GetDatabase().StringSetAsync(readKey, 600, TimeSpan.FromMinutes(15));
            await redis.GetDatabase().StringSetAsync(createKey, 20, TimeSpan.FromHours(1));
        }

        foreach (var path in new[] { "/api/institutions", "/api/institutions/alpha-temple", $"/api/institutions/alpha-temple/availability?month={MonthOf(Month)}" })
        {
            using var limited = await SendAsync(Anonymous(address), HttpMethod.Get, path);
            await AssertRefusedAsync(limited, HttpStatusCode.TooManyRequests, "too_many_requests");

            using var elsewhere = await SendAsync(Anonymous(), HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, elsewhere.StatusCode);
        }

        using var booking = await BookAsync(ClientFor(donor), await SlotAsync(database.AlphaInstitutionId), Month.AddDays(1));
        await AssertRefusedAsync(booking, HttpStatusCode.TooManyRequests, "too_many_requests");
        Assert.Empty((await MineAsync(donor)).EnumerateArray());
    }
}

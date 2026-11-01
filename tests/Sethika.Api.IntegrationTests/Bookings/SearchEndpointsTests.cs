using System.Globalization;
using System.Net;
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
using Sethika.Application.Bookings.Slots;
using Sethika.Domain.Bookings;
using Sethika.Domain.Institutions;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Messaging;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// The cross-institution search over HTTP (plan 06, B.1), against real SQL Server and Redis: who is
/// found, which slots, the time of day, the filters, distance and the radius edge, the same answer
/// with the geo index down or missing, the index following the outbox and the rebuild, the search
/// log, and every refusal.
/// </summary>
/// <remarks>
/// Institutions and slots are written straight into the tables, as the platform, so each test states
/// the places it needs. Each test's host keeps its geo index under a key of its own, so no test sees
/// another's index or the one a running API keeps in the same Redis database.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class SearchEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "search-test-signing-key-at-least-32-bytes";

    private const string SearchPath = "/api/search";

    private const string MonthPath = "/api/search/month";

    private const string LookupsPath = "/api/search/lookups";

    /// <summary>The rule name of <c>BookingRateLimits.PublicReadPerClient</c>, which is internal to the Api.</summary>
    private const string PublicReadRule = "institution-read-client";

    /// <summary>A Redis nobody listens on: connections are refused at once.</summary>
    private const string DeadRedis = "127.0.0.1:1,connectTimeout=500,syncTimeout=500,asyncTimeout=500,abortConnect=false";

    /// <summary>Colombo Fort, the centre of the searches near a point.</summary>
    private static readonly GeoPoint Centre = new(6.9271, 79.8612);

    private readonly string _geoKey = $"geo:test:{Guid.NewGuid():N}";
    private readonly List<string> _addresses = [];
    private WebApplicationFactory<Program> _host = null!;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    /// <summary>Ten days ahead: inside every slot's default 1-90 day window, whatever today is.</summary>
    private static DateOnly Day => Today.AddDays(10);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _host = Host(database.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();

        await using var redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
        await redis.GetDatabase().KeyDeleteAsync([_geoKey, .. _addresses.Select(a => (RedisKey)RateLimitKey.For(PublicReadRule, a))]);

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private WebApplicationFactory<Program> Host(string redis) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", redis);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("GeoIndex:Key", _geoKey);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackProxyStartupFilter()));
        });

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

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>A point <paramref name="northKm"/> north and <paramref name="eastKm"/> east of <paramref name="from"/>, to the 6 decimals an address keeps.</summary>
    private static GeoPoint Offset(GeoPoint from, double northKm, double eastKm)
    {
        var kmPerDegree = 2 * Math.PI * GeoPoint.EarthRadiusKm / 360;
        return new GeoPoint(
            Math.Round(from.Latitude + (northKm / kmPerDegree), 6),
            Math.Round(from.Longitude + (eastKm / (kmPerDegree * Math.Cos(from.Latitude * Math.PI / 180))), 6));
    }

    private sealed record Place(int Id, string Slug, string Code, GeoPoint Point);

    /// <summary>An institution, written as the platform with the draft application it must point at.</summary>
    private async Task<Place> InstitutionAsync(
        string name,
        GeoPoint? at = null,
        string type = "TEMPLE",
        string district = "CMB",
        string city = "Colombo",
        string status = "Active",
        bool listed = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var point = at ?? Centre;
        var code = "S-" + suffix;
        var slug = name.ToLowerInvariant().Replace(' ', '-') + "-" + suffix;

        await using var connection = await PlatformConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            DECLARE @typeId SMALLINT = (SELECT Id FROM org.InstitutionTypes WHERE Code = @type);
            DECLARE @districtId SMALLINT = (SELECT Id FROM core.Districts WHERE Code = @district);

            INSERT org.RegistrationRequests
                (RequestNo, ApplicantUserId, InstitutionTypeId, ProposedName, OfficialRegistrationNo,
                 RegisteringAuthority, RegistrationDate, AddressLine1, City, DistrictId, Latitude, Longitude,
                 OfficePhone, ApplicantFullName, ApplicantNic, ApplicantDesignation, ApplicantPhone, Status)
            VALUES (@requestNo, @applicant, @typeId, @name, @code, N'Authority', '2024-01-01', N'1 Search Road',
                    @city, @districtId, @latitude, @longitude, '0111000009', N'Search Steward', '901234569V',
                    N'Trustee', '0771000009', 'Draft');

            INSERT org.Institutions
                (InstitutionCode, Slug, RegistrationRequestId, InstitutionTypeId, Name, OfficialRegistrationNo,
                 AddressLine1, City, DistrictId, Latitude, Longitude, Phone, Status, IsPubliclyListed,
                 ApprovedBy, ApprovedAt)
            OUTPUT inserted.Id
            SELECT @code, @slug, r.Id, @typeId, @name, @code, N'1 Search Road', @city, @districtId,
                   @latitude, @longitude, '0111000009', @status, @listed, @applicant, SYSUTCDATETIME()
            FROM org.RegistrationRequests AS r WHERE r.RequestNo = @requestNo;
            """,
            new
            {
                type,
                district,
                requestNo = "REQ-S-" + suffix,
                applicant = SethikaTestDatabase.AlphaUserId,
                name,
                code,
                slug,
                city,
                latitude = (decimal)point.Latitude,
                longitude = (decimal)point.Longitude,
                status,
                listed,
            });

        return new Place(id, slug, code, point);
    }

    /// <summary>A slot of <paramref name="place"/>, written as the platform; its public id.</summary>
    private async Task<Guid> SlotAsync(
        Place place,
        string start = "11:00",
        string end = "12:30",
        string? name = null,
        string mode = "Exclusive",
        byte maxSponsors = 1,
        short minAdvanceDays = 1,
        byte days = MealSlot.EveryDay,
        bool active = true,
        short sortOrder = 1)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, NameSi, StartTime, EndTime, DaysOfWeekMask, BookingMode, MaxSponsors,
                 MinHeadcount, MaxHeadcount, MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment,
                 SortOrder, IsActive, CreatedBy)
            VALUES (@publicId, @institutionId, @name, @nameSi, @start, @end, @days, @mode, @maxSponsors,
                    5, 50, @minAdvanceDays, 90, 1, 'DonorBrings', @sortOrder, @active, @createdBy);
            """,
            new
            {
                publicId,
                institutionId = place.Id,
                name = name ?? "Dana " + start,
                nameSi = "දානය " + start,
                start,
                end,
                days,
                mode,
                maxSponsors,
                minAdvanceDays,
                sortOrder,
                active,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    /// <summary>Takes <paramref name="sponsors"/> places and <paramref name="headcount"/> people of the slot-day, as bookings would.</summary>
    private Task BookedAsync(Guid slot, DateOnly date, byte sponsors, short headcount, string status = "Open") =>
        ExecuteAsync(
            """
            INSERT booking.SlotInstances (InstitutionId, MealSlotId, SlotDate, MaxSponsors, MaxHeadcount, BookedSponsors, BookedHeadcount, Status)
            SELECT s.InstitutionId, s.Id, @date, s.MaxSponsors, s.MaxHeadcount, @sponsors, @headcount, @status
            FROM booking.MealSlots AS s WHERE s.PublicId = @slot;
            """,
            new { slot, date = date.ToDateTime(TimeOnly.MinValue), sponsors, headcount, status });

    private Task BlockAsync(Place place, DateOnly date, Guid? slot = null) =>
        ExecuteAsync(
            """
            INSERT booking.BlockedDates (InstitutionId, MealSlotId, DateFrom, DateTo, Reason, CreatedBy)
            VALUES (@institutionId, (SELECT s.Id FROM booking.MealSlots AS s WHERE s.PublicId = @slot), @date, @date, N'Festival', @createdBy);
            """,
            new { institutionId = place.Id, slot, date = date.ToDateTime(TimeOnly.MinValue), createdBy = SethikaTestDatabase.AlphaUserId });

    private Task SeasonAsync(Guid slot, DateOnly from, DateOnly to) =>
        ExecuteAsync(
            """
            INSERT booking.MealSlotSeasons (MealSlotId, Name, DateFrom, DateTo)
            SELECT s.Id, N'Season', @from, @to FROM booking.MealSlots AS s WHERE s.PublicId = @slot;
            """,
            new { slot, from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue) });

    private Task SetInstitutionAsync(Place place, string status = "Active", bool listed = true) =>
        ExecuteAsync(
            "UPDATE org.Institutions SET Status = @status, IsPubliclyListed = @listed WHERE Id = @id;",
            new { id = place.Id, status, listed });

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Near(GeoPoint point) =>
        string.Create(CultureInfo.InvariantCulture, $"{point.Latitude},{point.Longitude}");

    /// <summary>A GET from <paramref name="address"/>, or a fresh client address so the per-address limit is never shared.</summary>
    private async Task<HttpResponseMessage> GetAsync(string query, WebApplicationFactory<Program>? host = null, string path = SearchPath, string? address = null)
    {
        address ??= $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
        _addresses.Add(address);

        using var client = (host ?? _host).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        return await client.GetAsync(path + "?" + query);
    }

    /// <summary>The raw answer of a search that succeeds, so two can be compared byte for byte.</summary>
    private async Task<string> SearchTextAsync(string query, WebApplicationFactory<Program>? host = null, string path = SearchPath)
    {
        using var response = await GetAsync(query, host, path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query} answered {(int)response.StatusCode}: {text}");
        return text;
    }

    private async Task<JsonElement> SearchAsync(string query, WebApplicationFactory<Program>? host = null) =>
        JsonDocument.Parse(await SearchTextAsync(query, host)).RootElement;

    private async Task<JsonElement> MonthAsync(string query, WebApplicationFactory<Program>? host = null) =>
        JsonDocument.Parse(await SearchTextAsync(query, host, MonthPath)).RootElement;

    /// <summary>The month view's counts, by date.</summary>
    private static Dictionary<DateOnly, int> Counts(JsonElement month) =>
        month.GetProperty("days").EnumerateArray().ToDictionary(
            d => DateOnly.ParseExact(d.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            d => d.GetProperty("institutions").GetInt32());

    private async Task<string[]> RefusedAsync(string query, string path = SearchPath)
    {
        using var response = await GetAsync(query, path: path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalid_search", body.GetProperty("code").GetString());
        return [.. body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!)];
    }

    private static string[] Slugs(JsonElement search) =>
        [.. search.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("slug").GetString()!)];

    private static JsonElement Result(JsonElement search, Place place) =>
        search.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("slug").GetString() == place.Slug);

    private static Guid[] SlotIds(JsonElement result) =>
        [.. result.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("mealSlot").GetGuid())];

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    private async Task<int> RebuildAsync()
    {
        await using var scope = _host.Services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        return await scope.ServiceProvider.GetRequiredService<IInstitutionGeoIndex>().RebuildAsync();
    }

    private async Task<int[]> MembersAsync()
    {
        var members = await _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().SortedSetRangeByRankAsync(_geoKey);
        return [.. members.Select(m => int.Parse(m.ToString(), CultureInfo.InvariantCulture)).Order()];
    }

    private Task<bool> ForgetAsync(Place place) =>
        _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().SortedSetRemoveAsync(_geoKey, place.Id.ToString(CultureInfo.InvariantCulture));

    private Task<bool> DropIndexAsync() =>
        _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync(_geoKey);

    private async Task SetListedAsync(Place place, bool listed)
    {
        await using var scope = _host.Services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        var result = await scope.ServiceProvider.GetRequiredService<SlotManagementHandler>().SetListedAsync(place.Id, SethikaTestDatabase.AlphaUserId, listed);
        Assert.True(result.Succeeded, result.Error);
    }

    private async Task RunOutboxAsync()
    {
        await using var scope = _host.Services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().RunAsync();
    }

    // ------------------------------------------------------------------ who is found

    [Fact]
    public async Task OnlyActiveListedInstitutionsWithAnOpenSlotAreFound_WithNothingButWhatThePublicPageShows()
    {
        var lotus = await InstitutionAsync("Lotus Temple");
        var open = await SlotAsync(lotus, "11:00", "12:30", name: "Noon Dana");

        var banyan = await InstitutionAsync("Banyan Kovil", type: "KOVIL");
        await SlotAsync(banyan);

        await SlotAsync(await InstitutionAsync("Hidden Temple", listed: false));
        await SlotAsync(await InstitutionAsync("Suspended Temple", status: "Suspended"));
        await SlotAsync(await InstitutionAsync("Onboarding Temple", status: "Onboarding"));
        await SlotAsync(await InstitutionAsync("Idle Temple"), active: false);
        await InstitutionAsync("Empty Temple");

        // The seeded Alpha and Beta are active and have a slot, but are not listed.
        var search = await SearchAsync($"date={Iso(Day)}");

        Assert.Equal(Iso(Day), search.GetProperty("date").GetString());
        Assert.False(search.GetProperty("more").GetBoolean());
        Assert.Equal([banyan.Slug, lotus.Slug], Slugs(search));

        var result = Result(search, lotus);
        Assert.Equal(
            ["city", "distanceKm", "district", "districtSi", "districtTa", "latitude", "longitude", "name", "nameSi", "nameTa",
             "slots", "slug", "type", "typeName", "typeNameSi", "typeNameTa"],
            Names(result));
        Assert.Equal(("Lotus Temple", "TEMPLE", "Colombo"), (result.GetProperty("name").GetString(), result.GetProperty("type").GetString(), result.GetProperty("city").GetString()));
        Assert.Equal((decimal)Centre.Latitude, result.GetProperty("latitude").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("distanceKm").ValueKind);

        var slot = Assert.Single(result.GetProperty("slots").EnumerateArray());
        Assert.Equal(["endTime", "headcount", "mealSlot", "name", "nameSi", "nameTa", "sponsors", "startTime"], Names(slot));
        Assert.Equal(open, slot.GetProperty("mealSlot").GetGuid());
        Assert.Equal(("Noon Dana", "11:00", "12:30", 1, 50),
            (slot.GetProperty("name").GetString(), slot.GetProperty("startTime").GetString(), slot.GetProperty("endTime").GetString(),
             slot.GetProperty("sponsors").GetInt32(), slot.GetProperty("headcount").GetInt32()));
    }

    [Fact]
    public async Task ASlotIsShownOnlyOnADayItCanBeBooked_AndAnInstitutionWithNoneIsNotShown()
    {
        var temple = await InstitutionAsync("Ruwan Temple");
        var open = await SlotAsync(temple, "06:00", "06:30", sortOrder: 1);
        var shared = await SlotAsync(temple, "07:00", "07:30", mode: "Shared", maxSponsors: 4, sortOrder: 2);
        var outOfSeason = await SlotAsync(temple, "08:00", "08:30", sortOrder: 3);
        var inSeason = await SlotAsync(temple, "09:00", "09:30", sortOrder: 4);
        var blocked = await SlotAsync(temple, "10:00", "10:30", sortOrder: 5);
        var full = await SlotAsync(temple, "11:00", "11:30", sortOrder: 6);
        var closed = await SlotAsync(temple, "12:00", "12:30", mode: "Shared", maxSponsors: 4, sortOrder: 7);
        var tooSoon = await SlotAsync(temple, "13:00", "13:30", minAdvanceDays: 30, sortOrder: 8);
        var otherDay = await SlotAsync(temple, "14:00", "14:30", days: MealSlot.DayBit(Day.AddDays(1).DayOfWeek), sortOrder: 9);

        await BookedAsync(shared, Day, sponsors: 1, headcount: 12);
        await SeasonAsync(outOfSeason, Day.AddDays(20), Day.AddDays(40));
        await SeasonAsync(inSeason, Day.AddDays(-2), Day.AddDays(2));
        await BlockAsync(temple, Day, blocked);
        await BookedAsync(full, Day, sponsors: 1, headcount: 10);
        await BookedAsync(closed, Day, sponsors: 1, headcount: 10, status: "Closed");

        // Its only slot is taken on the day: not a result at all.
        var taken = await InstitutionAsync("Taken Temple");
        await BookedAsync(await SlotAsync(taken), Day, sponsors: 1, headcount: 10);

        // Closed for the whole day.
        var closedDay = await InstitutionAsync("Closed Temple");
        await SlotAsync(closedDay);
        await BlockAsync(closedDay, Day);

        var search = await SearchAsync($"date={Iso(Day)}");

        Assert.Equal([temple.Slug], Slugs(search));
        var result = Result(search, temple);
        Assert.Equal([open, shared, inSeason], SlotIds(result));

        var left = result.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("mealSlot").GetGuid() == shared);
        Assert.Equal((3, 38), (left.GetProperty("sponsors").GetInt32(), left.GetProperty("headcount").GetInt32()));

        // The day after, the blocks, the bookings and the season end are behind; the 14:00 slot runs.
        var next = Result(await SearchAsync($"date={Iso(Day.AddDays(1))}"), temple);
        Assert.Equal([open, shared, inSeason, blocked, full, closed, otherDay], SlotIds(next));
        Assert.DoesNotContain(tooSoon, SlotIds(next));
    }

    [Fact]
    public async Task TheTimeOfDayComesFromTheSlotsStartTime()
    {
        var temple = await InstitutionAsync("Time Temple");
        var lastMorning = await SlotAsync(temple, "10:29", "11:00", sortOrder: 1);
        var firstMidday = await SlotAsync(temple, "10:30", "11:30", sortOrder: 2);
        var lastMidday = await SlotAsync(temple, "14:59", "15:30", sortOrder: 3);
        var firstEvening = await SlotAsync(temple, "15:00", "16:00", sortOrder: 4);

        async Task<Guid[]> SlotsAt(string time) => SlotIds(Result(await SearchAsync($"date={Iso(Day)}&time={time}"), temple));

        Assert.Equal([lastMorning], await SlotsAt("morning"));
        Assert.Equal([firstMidday, lastMidday], await SlotsAt("midday"));
        Assert.Equal([firstEvening], await SlotsAt("EVENING"));
        Assert.Equal([lastMorning, firstMidday, lastMidday, firstEvening], await SlotsAt("any"));
        Assert.Equal([lastMorning, firstMidday, lastMidday, firstEvening], SlotIds(Result(await SearchAsync($"date={Iso(Day)}"), temple)));

        // Nothing at that time of day: the institution is not a result.
        await ExecuteAsync("UPDATE booking.MealSlots SET IsActive = 0 WHERE PublicId = @firstEvening;", new { firstEvening });
        Assert.Empty(Slugs(await SearchAsync($"date={Iso(Day)}&time=evening")));
    }

    [Fact]
    public async Task TheTypeAndTheDistrictNarrowTheSearch()
    {
        var colomboTemple = await InstitutionAsync("A Colombo Temple");
        var colomboKovil = await InstitutionAsync("B Colombo Kovil", type: "KOVIL");
        var galleTemple = await InstitutionAsync("C Galle Temple", at: new GeoPoint(6.0329, 80.2168), district: "GLL", city: "Galle");
        foreach (var place in new[] { colomboTemple, colomboKovil, galleTemple })
        {
            await SlotAsync(place);
        }

        Assert.Equal([colomboTemple.Slug, colomboKovil.Slug, galleTemple.Slug], Slugs(await SearchAsync($"date={Iso(Day)}")));
        Assert.Equal([colomboTemple.Slug, galleTemple.Slug], Slugs(await SearchAsync($"date={Iso(Day)}&type=TEMPLE")));
        Assert.Equal([galleTemple.Slug], Slugs(await SearchAsync($"date={Iso(Day)}&district=GLL")));
        Assert.Equal([colomboKovil.Slug], Slugs(await SearchAsync($"date={Iso(Day)}&type=KOVIL&district=CMB")));
        Assert.Empty(Slugs(await SearchAsync($"date={Iso(Day)}&type=MOSQUE")));

        var galle = Result(await SearchAsync($"date={Iso(Day)}&district=GLL"), galleTemple);
        Assert.Equal(("Galle", "Buddhist Temple"), (galle.GetProperty("district").GetString(), galle.GetProperty("typeName").GetString()));

        Assert.Equal(["type"], await RefusedAsync($"date={Iso(Day)}&type=SYNAGOGUE"));
        Assert.Equal(["district"], await RefusedAsync($"date={Iso(Day)}&district=XYZ"));
    }

    // ------------------------------------------------------------------ distance

    [Fact]
    public async Task NearAPointTheNearestComeFirst_AndTheRadiusIsAnEdgeWhicheverPathFoundThem()
    {
        var places = await RingAsync();
        var five = $"date={Iso(Day)}&near={Near(Centre)}&radiusKm=5";

        // No index yet: SQL Server finds the candidates.
        var withoutIndex = await SearchTextAsync(five);

        Assert.Equal(7, await RebuildAsync());
        var withIndex = await SearchTextAsync(five);

        var search = JsonDocument.Parse(withIndex).RootElement;
        Place[] withinFive = [places.North1, places.TwinA, places.TwinB, places.Inside, places.Edge];
        Assert.Equal(withinFive.Select(p => p.Slug), Slugs(search));

        foreach (var place in withinFive)
        {
            Assert.Equal(Math.Round(Centre.DistanceKmTo(place.Point), 2), Result(search, place).GetProperty("distanceKm").GetDouble());
        }

        Assert.Equal(withIndex, withoutIndex);

        // The next radius takes in the one just outside and the one 20 km away, in order.
        string[] withinTwentyFive = [.. withinFive.Select(p => p.Slug), places.Outside.Slug, places.Far.Slug];
        Assert.Equal(withinTwentyFive, Slugs(await SearchAsync($"date={Iso(Day)}&near={Near(Centre)}&radiusKm=25")));

        // A point without a radius searches 10 km.
        string[] withinTen = [.. withinFive.Select(p => p.Slug), places.Outside.Slug];
        Assert.Equal(withinTen, Slugs(await SearchAsync($"date={Iso(Day)}&near={Near(Centre)}")));
    }

    private sealed record Ring(Place North1, Place TwinA, Place TwinB, Place Inside, Place Edge, Place Outside, Place Far);

    /// <summary>
    /// Seven listed temples around <see cref="Centre"/>: 1 km north; two at one point 3 km east (a
    /// tie, broken by name: B is written first, so the database's own order would put it first); one
    /// just inside 5 km to the south and one just outside; one 20 km away; and one on the edge to the
    /// east, inside by 0.4 m.
    /// </summary>
    /// <remarks>
    /// The edge is where the measures part. East-west, SQL Server's ellipsoid makes 4.9996 km on the
    /// sphere 5.005 km, and Redis's larger sphere makes it 5.001 km: both would leave it out of a
    /// 5 km circle of their own. Only fetching candidates from a wider circle and judging them on
    /// one sphere finds it on both paths.
    /// </remarks>
    private async Task<Ring> RingAsync()
    {
        var north1 = await InstitutionAsync("Ring North", Offset(Centre, 1, 0));
        var twinB = await InstitutionAsync("Twin B", Offset(Centre, 0, 3));
        var twinA = await InstitutionAsync("Twin A", Offset(Centre, 0, 3));
        var ring = new Ring(
            north1,
            twinA,
            twinB,
            await InstitutionAsync("Ring Inside", Offset(Centre, -4.97, 0)),
            await InstitutionAsync("Ring Edge", Offset(Centre, 0, 4.9996)),
            await InstitutionAsync("Ring Outside", Offset(Centre, -5.03, 0)),
            await InstitutionAsync("Ring Far", Offset(Centre, 20, 0)));

        Assert.InRange(Centre.DistanceKmTo(ring.Inside.Point), 4.95, 5);
        Assert.InRange(Centre.DistanceKmTo(ring.Edge.Point), 4.999, 5);
        Assert.InRange(Centre.DistanceKmTo(ring.Outside.Point), 5, 5.05);

        foreach (var place in new[] { ring.North1, ring.TwinA, ring.TwinB, ring.Inside, ring.Edge, ring.Outside, ring.Far })
        {
            await SlotAsync(place);
        }

        return ring;
    }

    [Fact]
    public async Task WithRedisDownTheSameSearchesGiveTheSameAnswers()
    {
        await RingAsync();
        var kovil = await InstitutionAsync("Ring Kovil", Offset(Centre, 2, 2), type: "KOVIL");
        await SlotAsync(kovil, "18:00", "19:00");
        Assert.Equal(8, await RebuildAsync());

        string[] queries =
        [
            $"date={Iso(Day)}&near={Near(Centre)}&radiusKm=5",
            $"date={Iso(Day)}&near={Near(Centre)}&radiusKm=10&time=evening",
            $"date={Iso(Day)}&near={Near(Offset(Centre, 10, 0))}&radiusKm=25&type=TEMPLE",
            $"date={Iso(Day)}&near={Near(Centre)}&radiusKm=50",
        ];

        await using var down = Host(DeadRedis);
        foreach (var query in queries)
        {
            var up = await SearchTextAsync(query);
            Assert.NotEqual("[]", JsonDocument.Parse(up).RootElement.GetProperty("results").GetRawText());
            Assert.Equal(up, await SearchTextAsync(query, down));
        }
    }

    [Fact]
    public async Task TheSearchAsksTheIndexWhenItCan_AndSqlServerWhenItCannot()
    {
        var kept = await InstitutionAsync("Index Kept", Offset(Centre, 1, 0));
        var forgotten = await InstitutionAsync("Index Forgotten", Offset(Centre, 2, 0));
        await SlotAsync(kept);
        await SlotAsync(forgotten);
        await RebuildAsync();

        var query = $"date={Iso(Day)}&near={Near(Centre)}&radiusKm=5";

        // The index is consulted: what it does not hold is not found, however listed it is.
        Assert.True(await ForgetAsync(forgotten));
        Assert.Equal([kept.Slug], Slugs(await SearchAsync(query)));

        // No index: SQL Server measures, and finds both.
        Assert.True(await DropIndexAsync());
        Assert.Equal([kept.Slug, forgotten.Slug], Slugs(await SearchAsync(query)));
    }

    [Fact]
    public async Task AnInstitutionUnlistedOrSuspendedSinceTheIndexWasBuiltIsNotFound()
    {
        var listed = await InstitutionAsync("Stale Listed", Offset(Centre, 1, 0));
        var unlisted = await InstitutionAsync("Stale Unlisted", Offset(Centre, 2, 0));
        var suspended = await InstitutionAsync("Stale Suspended", Offset(Centre, 3, 0));
        foreach (var place in new[] { listed, unlisted, suspended })
        {
            await SlotAsync(place);
        }

        await RebuildAsync();

        // Changed in SQL Server, with no outbox run: the index still holds them.
        await SetInstitutionAsync(unlisted, listed: false);
        await SetInstitutionAsync(suspended, status: "Suspended");
        Assert.Equal([listed.Id, unlisted.Id, suspended.Id], await MembersAsync());

        Assert.Equal([listed.Slug], Slugs(await SearchAsync($"date={Iso(Day)}&near={Near(Centre)}&radiusKm=5")));
    }

    [Fact]
    public async Task TheSqlServerFallbackReadsOnlyTheCircleAndTheIdsItIsGiven()
    {
        var ring = await RingAsync();

        await using var scope = _host.Services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDiscoveryQueries>();

        int[] withinFour = [.. new[] { ring.North1.Id, ring.TwinA.Id, ring.TwinB.Id }.Order()];

        var circle = await queries.GetSearchSourceAsync(new SearchSourceFilter(null, Centre, 4, null, null), Day, Day);
        Assert.Equal(withinFour, circle.Institutions.Select(i => i.Id).Order());

        // The slots read are the candidates' only.
        Assert.Equal(withinFour, circle.Slots.Select(s => s.InstitutionId).Order());

        var named = await queries.GetSearchSourceAsync(new SearchSourceFilter([ring.Far.Id, ring.Inside.Id], null, null, null, null), Day, Day);
        Assert.Equal(new[] { ring.Inside.Id, ring.Far.Id }.Order(), named.Institutions.Select(i => i.Id).Order());

        var none = await queries.GetSearchSourceAsync(new SearchSourceFilter([], null, null, null, null), Day, Day);
        Assert.Empty(none.Institutions);
        Assert.Empty(none.Slots);
    }

    // ------------------------------------------------------------------ the index follows SQL Server

    [Fact]
    public async Task TheIndexFollowsTheListingThroughTheOutbox()
    {
        var temple = await InstitutionAsync("Outbox Temple", Offset(Centre, 1, 0));
        var other = await InstitutionAsync("Outbox Other", Offset(Centre, 2, 0));
        await SlotAsync(temple);
        await SlotAsync(other);
        await RebuildAsync();
        Assert.Equal([temple.Id, other.Id], await MembersAsync());

        await SetListedAsync(temple, listed: false);

        await using (var connection = await PlatformConnectionAsync())
        {
            var message = await connection.QuerySingleAsync<(string EventType, string AggregateType, string AggregateId, string Payload)>(
                "SELECT EventType, AggregateType, AggregateId, Payload FROM core.OutboxMessages WHERE AggregateType = 'Institution' AND ProcessedAt IS NULL;");
            Assert.Equal((SlotManagementHandler.ListingChangedEvent, "Institution", temple.Code), (message.EventType, message.AggregateType, message.AggregateId));
            Assert.Equal(temple.Id, JsonDocument.Parse(message.Payload).RootElement.GetProperty("institutionId").GetInt32());
        }

        // Until the outbox runs the index still holds it; the search does not show it either way.
        Assert.Equal([temple.Id, other.Id], await MembersAsync());
        await RunOutboxAsync();
        Assert.Equal([other.Id], await MembersAsync());

        await SetListedAsync(temple, listed: true);
        await RunOutboxAsync();
        Assert.Equal([temple.Id, other.Id], await MembersAsync());
        Assert.Equal([temple.Slug, other.Slug], Slugs(await SearchAsync($"date={Iso(Day)}&near={Near(Centre)}&radiusKm=5")));

        // Setting what is already set changes nothing and writes no message.
        await SetListedAsync(temple, listed: true);
        await using (var connection = await PlatformConnectionAsync())
        {
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateType = 'Institution' AND ProcessedAt IS NULL;"));
        }
    }

    [Fact]
    public async Task ARefreshWithNoIndexBuildsTheWholeIndex_NotOneMember()
    {
        var first = await InstitutionAsync("Refresh First", Offset(Centre, 1, 0));
        var second = await InstitutionAsync("Refresh Second", Offset(Centre, 2, 0));
        await SlotAsync(first);
        await SlotAsync(second);

        await SetListedAsync(first, listed: false);
        await SetListedAsync(first, listed: true);
        await RunOutboxAsync();

        Assert.Equal([first.Id, second.Id], await MembersAsync());
    }

    [Fact]
    public async Task ARebuildHoldsExactlyTheActiveListedInstitutions_AndRestoresWhatWasLost()
    {
        var listed = await InstitutionAsync("Rebuild Listed", Offset(Centre, 1, 0));
        var other = await InstitutionAsync("Rebuild Other", Offset(Centre, 2, 0));
        await InstitutionAsync("Rebuild Unlisted", listed: false);
        await InstitutionAsync("Rebuild Suspended", status: "Suspended");
        var deleted = await InstitutionAsync("Rebuild Deleted");
        await ExecuteAsync("UPDATE org.Institutions SET IsDeleted = 1 WHERE Id = @id;", new { id = deleted.Id });

        // Listed or not, with a slot or not: the index is about where, the search about when.
        Assert.Equal(2, await RebuildAsync());
        Assert.Equal([listed.Id, other.Id], await MembersAsync());

        var position = (await _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().GeoPositionAsync(_geoKey, listed.Id.ToString(CultureInfo.InvariantCulture)))!.Value;
        Assert.Equal(listed.Point.Latitude, position.Latitude, 0.00001);
        Assert.Equal(listed.Point.Longitude, position.Longitude, 0.00001);

        Assert.True(await ForgetAsync(other));
        Assert.Equal(2, await RebuildAsync());
        Assert.Equal([listed.Id, other.Id], await MembersAsync());

        // And drops what SQL Server no longer lists.
        await SetInstitutionAsync(other, listed: false);
        Assert.Equal(1, await RebuildAsync());
        Assert.Equal([listed.Id], await MembersAsync());

        // Nobody listed: no index, so a search asks SQL Server rather than trusting an empty circle.
        await SetInstitutionAsync(listed, listed: false);
        Assert.Equal(0, await RebuildAsync());
        Assert.False(await _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyExistsAsync(_geoKey));
    }

    // ------------------------------------------------------------------ the log, the cap and refusals

    [Fact]
    public async Task EachSearchIsLogged_WithNeitherWhoNorWhere()
    {
        var temple = await InstitutionAsync("Log Temple", Offset(Centre, 1, 0));
        await SlotAsync(temple);
        await RebuildAsync();

        await SearchAsync($"date={Iso(Day)}&near={Near(Centre)}&radiusKm=25&type=TEMPLE");
        await SearchAsync($"date={Iso(Day.AddDays(1))}&district=CMB");
        await SearchAsync($"date={Iso(Day.AddDays(2))}&type=MOSQUE");
        await RefusedAsync($"date={Iso(Day)}&radiusKm=7&near={Near(Centre)}");

        await using var connection = await PlatformConnectionAsync();
        var rows = (await connection.QueryAsync<(DateTime TargetDate, string? Type, string? District, short? RadiusKm, short ResultCount, Guid? UserId)>(
            """
            SELECT s.TargetDate, t.Code, d.Code, s.RadiusKm, s.ResultCount, s.UserId
            FROM booking.AvailabilitySearches AS s
            LEFT JOIN org.InstitutionTypes AS t ON t.Id = s.InstitutionTypeId
            LEFT JOIN core.Districts AS d ON d.Id = s.DistrictId
            ORDER BY s.Id;
            """)).ToList();

        (DateTime, string?, string?, int?, int, Guid?)[] expected =
        [
            (Day.ToDateTime(TimeOnly.MinValue), "TEMPLE", null, 25, 1, null),
            (Day.AddDays(1).ToDateTime(TimeOnly.MinValue), null, "CMB", null, 1, null),
            (Day.AddDays(2).ToDateTime(TimeOnly.MinValue), "MOSQUE", null, null, 0, null),
        ];
        Assert.Equal(expected, rows.Select(r => (r.TargetDate, r.Type, r.District, (int?)r.RadiusKm, (int)r.ResultCount, r.UserId)));
    }

    [Fact]
    public async Task AtMostAHundredAreListed_AndTheAnswerSaysThereAreMore()
    {
        await ExecuteAsync(
            """
            DECLARE @typeId SMALLINT = (SELECT Id FROM org.InstitutionTypes WHERE Code = 'TEMPLE');
            DECLARE @districtId SMALLINT = (SELECT Id FROM core.Districts WHERE Code = 'CMB');

            WITH n AS (SELECT TOP (101) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i FROM sys.all_objects)
            INSERT org.RegistrationRequests
                (RequestNo, ApplicantUserId, InstitutionTypeId, ProposedName, OfficialRegistrationNo,
                 RegisteringAuthority, RegistrationDate, AddressLine1, City, DistrictId, Latitude, Longitude,
                 OfficePhone, ApplicantFullName, ApplicantNic, ApplicantDesignation, ApplicantPhone, Status)
            SELECT CONCAT('REQ-CAP-', FORMAT(n.i, '000')), @applicant, @typeId, CONCAT(N'Cap ', FORMAT(n.i, '000')),
                   CONCAT('CAP-', FORMAT(n.i, '000')), N'Authority', '2024-01-01', N'1 Cap Road', N'Colombo',
                   @districtId, 6.9, 79.9, '0111000009', N'Cap Steward', '901234569V', N'Trustee', '0771000009', 'Draft'
            FROM n;

            INSERT org.Institutions
                (InstitutionCode, Slug, RegistrationRequestId, InstitutionTypeId, Name, OfficialRegistrationNo,
                 AddressLine1, City, DistrictId, Latitude, Longitude, Phone, Status, IsPubliclyListed, ApprovedBy, ApprovedAt)
            SELECT r.OfficialRegistrationNo, LOWER(r.OfficialRegistrationNo), r.Id, @typeId, r.ProposedName, r.OfficialRegistrationNo,
                   N'1 Cap Road', N'Colombo', @districtId, 6.9, 79.9, '0111000009', 'Active', 1, @applicant, SYSUTCDATETIME()
            FROM org.RegistrationRequests AS r WHERE r.RequestNo LIKE 'REQ-CAP-%';

            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, MaxHeadcount, CreatedBy)
            SELECT NEWID(), i.Id, N'Cap Dana', '11:00', '12:00', 50, @applicant
            FROM org.Institutions AS i WHERE i.InstitutionCode LIKE 'CAP-%';
            """,
            new { applicant = SethikaTestDatabase.AlphaUserId });

        var search = await SearchAsync($"date={Iso(Day)}&district=CMB");

        var slugs = Slugs(search);
        Assert.Equal(100, slugs.Length);
        Assert.Equal(("cap-000", "cap-099"), (slugs[0], slugs[^1]));
        Assert.True(search.GetProperty("more").GetBoolean());

        await ExecuteAsync("UPDATE org.Institutions SET IsPubliclyListed = 0 WHERE InstitutionCode = 'CAP-100';");
        Assert.False((await SearchAsync($"date={Iso(Day)}&district=CMB")).GetProperty("more").GetBoolean());
    }

    [Fact]
    public async Task ASearchThatCannotBeRunIsRefused_AndSaysWhy()
    {
        var day = Iso(Day);
        var near = Near(Centre);

        Assert.Equal(["date"], await RefusedAsync(""));
        Assert.Equal(["date"], await RefusedAsync("date=14-03-2027"));
        Assert.Equal(["date"], await RefusedAsync($"date={Iso(Today.AddDays(-1))}"));
        Assert.Equal(["time"], await RefusedAsync($"date={day}&time=brunch"));
        Assert.Equal(["near"], await RefusedAsync($"date={day}&near=abc"));
        Assert.Equal(["near"], await RefusedAsync($"date={day}&near=6.9"));
        Assert.Equal(["near"], await RefusedAsync($"date={day}&near=13.0827,80.2707"));
        Assert.Equal(["near"], await RefusedAsync($"date={day}&near=NaN,80"));
        Assert.Equal(["radiusKm"], await RefusedAsync($"date={day}&near={near}&radiusKm=7"));
        Assert.Equal(["radiusKm"], await RefusedAsync($"date={day}&near={near}&radiusKm=ten"));
        Assert.Equal(["radiusKm"], await RefusedAsync($"date={day}&radiusKm=10"));
        Assert.Equal(["district"], await RefusedAsync($"date={day}&near={near}&district=CMB"));
        Assert.Equal(["type"], await RefusedAsync($"date={day}&type={new string('T', 31)}"));
        Assert.Equal(["district"], await RefusedAsync($"date={day}&district=COLOMBODIST"));

        // Several at once, all named.
        Assert.Equal(["date", "near", "radiusKm"], await RefusedAsync("date=soon&near=x&radiusKm=y"));

        // Today is searchable: what is left of it.
        using var today = await GetAsync($"date={Iso(Today)}");
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
    }

    [Fact]
    public async Task SearchingSharesThePublicReadBudgetOfAnAddress()
    {
        var address = $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
        _addresses.Add(address);

        // The budget of the institution reads, already spent this window.
        await _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .StringSetAsync(RateLimitKey.For(PublicReadRule, address), 600, TimeSpan.FromMinutes(15));

        foreach (var (query, path) in new[] { ($"date={Iso(Day)}", SearchPath), ($"month={MonthOf(NextMonth)}", MonthPath), ("", LookupsPath) })
        {
            using var limited = await GetAsync(query, path: path, address: address);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("too_many_requests", JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

            using var elsewhere = await GetAsync(query, path: path);
            Assert.Equal(HttpStatusCode.OK, elsewhere.StatusCode);
        }
    }

    [Fact]
    public async Task TheLookupsOfferEveryActiveTypeAndEveryDistrict_InThreeLanguages()
    {
        await using var connection = await PlatformConnectionAsync();
        var types = (await connection.QueryAsync<string>("SELECT Code FROM org.InstitutionTypes WHERE IsActive = 1 ORDER BY Name;")).ToArray();
        var districts = (await connection.QueryAsync<string>("SELECT Code FROM core.Districts ORDER BY Name;")).ToArray();

        var lookups = JsonDocument.Parse(await SearchTextAsync("", path: LookupsPath)).RootElement;
        string[] Codes(string list) => [.. lookups.GetProperty(list).EnumerateArray().Select(e => e.GetProperty("code").GetString()!)];

        Assert.Equal(types, Codes("types"));
        Assert.Equal(districts, Codes("districts"));
        Assert.Equal(25, districts.Length);
        Assert.Equal(["category", "code", "name", "nameSi", "nameTa"], Names(lookups.GetProperty("types")[0]));
        Assert.Equal(["code", "name", "nameSi", "nameTa"], Names(lookups.GetProperty("districts")[0]));

        var galle = lookups.GetProperty("districts").EnumerateArray().Single(d => d.GetProperty("code").GetString() == "GLL");
        Assert.Equal("Galle", galle.GetProperty("name").GetString());

        // A retired type is not offered: no listed institution can be one. Each name comes in its own
        // language's field; the seed has none yet, so one is given here. Reference data, so put back by hand.
        var (retired, named) = (types[^1], types[0]);
        var before = await connection.QuerySingleAsync<(string? TypeSi, string? TypeTa, string? GalleSi, string? GalleTa)>(
            "SELECT t.NameSi, t.NameTa, d.NameSi, d.NameTa FROM org.InstitutionTypes AS t CROSS JOIN core.Districts AS d WHERE t.Code = @named AND d.Code = 'GLL';",
            new { named });
        await connection.ExecuteAsync(
            """
            UPDATE org.InstitutionTypes SET IsActive = 0 WHERE Code = @retired;
            UPDATE org.InstitutionTypes SET NameSi = N'වර්ගය', NameTa = N'வகை' WHERE Code = @named;
            UPDATE core.Districts SET NameSi = N'ගාල්ල', NameTa = N'காலி' WHERE Code = 'GLL';
            """,
            new { retired, named });
        try
        {
            var after = JsonDocument.Parse(await SearchTextAsync("", path: LookupsPath)).RootElement;
            Assert.Equal(types[..^1], after.GetProperty("types").EnumerateArray().Select(e => e.GetProperty("code").GetString()!));

            static (string?, string?) Local(JsonElement e) => (e.GetProperty("nameSi").GetString(), e.GetProperty("nameTa").GetString());
            Assert.Equal(("වර්ගය", "வகை"), Local(after.GetProperty("types")[0]));
            Assert.Equal(("ගාල්ල", "காலி"), Local(after.GetProperty("districts").EnumerateArray().Single(d => d.GetProperty("code").GetString() == "GLL")));
        }
        finally
        {
            await connection.ExecuteAsync(
                """
                UPDATE org.InstitutionTypes SET IsActive = 1 WHERE Code = @retired;
                UPDATE org.InstitutionTypes SET NameSi = @TypeSi, NameTa = @TypeTa WHERE Code = @named;
                UPDATE core.Districts SET NameSi = @GalleSi, NameTa = @GalleTa WHERE Code = 'GLL';
                """,
                new { retired, named, before.TypeSi, before.TypeTa, before.GalleSi, before.GalleTa });
        }
    }

    // ------------------------------------------------------------------ the month view

    /// <summary>The first of the month 40 days ahead: every day of it is 10 to 71 days away, inside every slot's window.</summary>
    private static DateOnly NextMonth
    {
        get
        {
            var ahead = Today.AddDays(40);
            return new DateOnly(ahead.Year, ahead.Month, 1);
        }
    }

    private static string MonthOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task TheMonthCountsOnEachDayTheInstitutionsTheDaySearchFinds()
    {
        var month = NextMonth;

        await SlotAsync(await InstitutionAsync("Month Every Day"));
        await SlotAsync(await InstitutionAsync("Month Mondays"), days: MealSlot.DayBit(DayOfWeek.Monday));
        await SlotAsync(await InstitutionAsync("Month Evening"), "18:00", "19:00");
        await SlotAsync(await InstitutionAsync("Month Hidden", listed: false));

        var interrupted = await InstitutionAsync("Month Interrupted");
        var interruptedSlot = await SlotAsync(interrupted);
        await BlockAsync(interrupted, month.AddDays(4));
        await BookedAsync(interruptedSlot, month.AddDays(6), sponsors: 1, headcount: 10);

        var seasonal = await InstitutionAsync("Month Seasonal");
        await SeasonAsync(await SlotAsync(seasonal), month.AddDays(9), month.AddDays(14));

        var view = await MonthAsync($"month={MonthOf(month)}");
        Assert.Equal(MonthOf(month), view.GetProperty("month").GetString());

        var counts = Counts(view);
        Assert.Equal(DateTime.DaysInMonth(month.Year, month.Month), counts.Count);
        Assert.Equal(month, counts.Keys.Min());

        // By hand, so the month and the search cannot be wrong together.
        int Expected(DateOnly date) =>
            2
            + (date.DayOfWeek == DayOfWeek.Monday ? 1 : 0)
            + (date == month.AddDays(4) || date == month.AddDays(6) ? 0 : 1)
            + (date >= month.AddDays(9) && date <= month.AddDays(14) ? 1 : 0);

        Assert.All(counts, c => Assert.Equal(Expected(c.Key), c.Value));

        // And each day as the day search finds it.
        foreach (var (date, count) in counts)
        {
            Assert.Equal(count, Slugs(await SearchAsync($"date={Iso(date)}")).Length);
        }

        // The time of day narrows the month as it narrows the search.
        Assert.All(Counts(await MonthAsync($"month={MonthOf(month)}&time=evening")), c => Assert.Equal(1, c.Value));
    }

    [Fact]
    public async Task TheMonthTakesTheSearchsFilters_AndRedisDownGivesTheSameMonth()
    {
        await RingAsync();
        await SlotAsync(await InstitutionAsync("Month Kovil", Offset(Centre, 2, 0), type: "KOVIL"));
        await SlotAsync(await InstitutionAsync("Month Galle", new GeoPoint(6.0329, 80.2168), district: "GLL", city: "Galle"));
        Assert.Equal(9, await RebuildAsync());

        var month = MonthOf(NextMonth);

        // Every slot runs every day of a month inside its window, so each day counts the same.
        async Task<int> EachDayAsync(string query) => Counts(await MonthAsync($"month={month}&{query}")).Values.Distinct().Single();

        Assert.Equal(6, await EachDayAsync($"near={Near(Centre)}&radiusKm=5"));
        Assert.Equal(5, await EachDayAsync($"near={Near(Centre)}&radiusKm=5&type=TEMPLE"));
        Assert.Equal(8, await EachDayAsync($"near={Near(Centre)}&radiusKm=25"));
        Assert.Equal(1, await EachDayAsync("district=GLL"));
        Assert.Equal(9, await EachDayAsync("time=any"));

        await using var down = Host(DeadRedis);
        foreach (var query in new[] { $"month={month}&near={Near(Centre)}&radiusKm=5", $"month={month}&near={Near(Centre)}&radiusKm=25&type=TEMPLE" })
        {
            Assert.Equal(await SearchTextAsync(query, path: MonthPath), await SearchTextAsync(query, down, MonthPath));
        }
    }

    [Fact]
    public async Task InThisMonthTheDaysGoneCountNone()
    {
        // Booked a day ahead at the earliest: today is too soon as well.
        await SlotAsync(await InstitutionAsync("Month Now"));

        var counts = Counts(await MonthAsync($"month={MonthOf(Today)}"));

        Assert.Equal(DateTime.DaysInMonth(Today.Year, Today.Month), counts.Count);
        Assert.All(counts, c => Assert.Equal(c.Key > Today ? 1 : 0, c.Value));
    }

    [Fact]
    public async Task AMonthThatCannotBeShownIsRefused_AndNoMonthIsLogged()
    {
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var near = Near(Centre);

        Assert.Equal(["month"], await RefusedAsync("", MonthPath));
        Assert.Equal(["month"], await RefusedAsync("month=2027-13", MonthPath));
        Assert.Equal(["month"], await RefusedAsync($"month={Iso(thisMonth)}", MonthPath));
        Assert.Equal(["month"], await RefusedAsync($"month={MonthOf(thisMonth.AddMonths(-1))}", MonthPath));
        Assert.Equal(["month"], await RefusedAsync($"month={MonthOf(thisMonth.AddMonths(13))}", MonthPath));
        Assert.Equal(["month"], await RefusedAsync("month=9999-12", MonthPath));
        Assert.Equal(["radiusKm"], await RefusedAsync($"month={MonthOf(thisMonth)}&near={near}&radiusKm=7", MonthPath));
        Assert.Equal(["district"], await RefusedAsync($"month={MonthOf(thisMonth)}&near={near}&district=CMB", MonthPath));
        Assert.Equal(["type"], await RefusedAsync($"month={MonthOf(thisMonth)}&type=SYNAGOGUE", MonthPath));
        Assert.Equal(["district"], await RefusedAsync($"month={MonthOf(thisMonth)}&district=XYZ", MonthPath));
        Assert.Equal(["month", "near"], await RefusedAsync("month=soon&near=x", MonthPath));

        // The furthest month there is.
        var furthest = thisMonth.AddMonths(12);
        Assert.Equal(DateTime.DaysInMonth(furthest.Year, furthest.Month), Counts(await MonthAsync($"month={MonthOf(furthest)}")).Count);

        await using var connection = await PlatformConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.AvailabilitySearches;"));
    }
}

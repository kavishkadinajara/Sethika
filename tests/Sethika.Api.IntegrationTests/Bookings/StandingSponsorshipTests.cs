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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Sponsors;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 08, K2-K5 and K9: standing sponsorships against real SQL Server and Redis. Setting one up books
/// its window's days through the engine, approved, for the institution's donor; clashes are listed,
/// not refused; the nightly job books each day once as the window moves and never a day let go; a
/// skip and an end let days go in one transaction; and the races between the job and a skip or an end
/// are forced on the sponsorship's row lock.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them: the owner
/// holds every BOOKINGS.SPONSORS permission, staff only VIEW (K11). Slots need approval, so a booking
/// approved here was approved because it is a standing day. A slot's window starts tomorrow
/// (<c>MinAdvanceDays = 1</c>), so every day set up here is at least two days ahead.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class StandingSponsorshipTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "standing-sponsorship-test-signing-key-at-least-32-bytes";
    private const string Sponsorships = "/api/institution/sponsorships";

    private readonly List<Guid> _users = [];
    private readonly CountingWake _wake = new();
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;

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

            -- BOOKINGS is a core module; the seed turns it on for Alpha only.
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @beta, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m WHERE m.Code = 'BOOKINGS';
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _alphaOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");
        _alphaStaff = await MemberAsync(database.AlphaInstitutionId, "STAFF");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddSingleton<IStandingBookingWake>(_wake));
        });
    }

    public async Task DisposeAsync()
    {
        // The reseed clears sponsorships, their bookings, the donor book and the roles these users hold.
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Counts the times setting one up started the standing job.</summary>
    private sealed class CountingWake : IStandingBookingWake
    {
        public int Wakes { get; private set; }

        public void Wake() => Wakes++;
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

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
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

    private SethikaDbContext ContextFor(ICurrentUserContext user) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);

    /// <summary>00:45 in Colombo on <paramref name="today"/>, when the job runs.</summary>
    private static FixedTimeProvider ClockAt(DateOnly today) => new(SriLankaTime.ToUtc(today, new TimeOnly(0, 45)));

    private static StandingDayBooker Booker(SethikaDbContext context, TimeProvider clock) =>
        new(context, new NoLocks(), new SqlStandingLocks(context), new SqlSlotInventory(context), new SqlNumberSeries(context), clock);

    /// <summary>The standing job as it runs at 00:45 on <paramref name="today"/>, as the platform.</summary>
    private async Task<StandingRun> RunAtAsync(DateOnly today)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        var clock = ClockAt(today);
        return await new StandingBookingRun(context, Booker(context, clock), clock).RunAsync();
    }

    /// <summary>One day through the engine's standing door, as the platform, on <paramref name="today"/>.</summary>
    private async Task<StandingDayResult> BookDayAtAsync(int sponsorshipId, DateOnly date, DateOnly today)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        return await Booker(context, ClockAt(today)).BookDayAsync(sponsorshipId, date);
    }

    private static string RandomPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    private async Task<Guid> UserAsync(string userType, string? phone = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, N'Standing Test', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, userType, phone = phone ?? RandomPhone() });

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

    /// <summary>A person in <paramref name="institutionId"/>'s donor book, linked to <paramref name="userId"/> if given.</summary>
    private async Task<Guid> DonorAsync(int? institutionId = null, Guid? userId = null, string? phone = null)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT org.InstitutionDonors (PublicId, InstitutionId, UserId, FullName, Phone, Language, CreatedBy)
            VALUES (@publicId, @institutionId, @userId, N'Sunil Perera', @phone, 'si', @createdBy);
            """,
            new
            {
                publicId,
                institutionId = institutionId ?? database.AlphaInstitutionId,
                userId,
                phone = phone ?? RandomPhone(),
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    /// <summary>A lunch every day, approval needed, sponsors per day <paramref name="maxSponsors"/> (1: one sponsor takes the day), a 1-<paramref name="maxAdvance"/> day window.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync(short maxAdvance = 30, byte maxSponsors = 5, int? institutionId = null)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @institutionId, @name, '11:30', '13:00', @mode, @maxSponsors, 1, 200, 1, @maxAdvance, 1,
                    'DonorBrings,InstitutionPrepares', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId,
                institutionId = institutionId ?? database.AlphaInstitutionId,
                name = "Standing " + Guid.NewGuid().ToString("N")[..8],
                mode = maxSponsors == 1 ? "Exclusive" : "Shared",
                maxSponsors,
                maxAdvance,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return (id, publicId);
    }

    private HttpClient ClientFor(Guid userId, int? institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
    }

    private HttpClient Alpha(Guid? member = null) => ClientFor(member ?? _alphaOwner, database.AlphaInstitutionId);

    private HttpClient Beta() => ClientFor(_betaOwner, database.BetaInstitutionId);

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

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(fields, body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A sponsorship body: <paramref name="pattern"/> plus whatever fields the test gives.</summary>
    private static Dictionary<string, object?> Body(Guid donor, Guid slot, string pattern, DateOnly start, DateOnly? end = null, short headcount = 25) =>
        new()
        {
            ["donor"] = donor,
            ["mealSlot"] = slot,
            ["pattern"] = pattern,
            ["startDate"] = Iso(start),
            ["endDate"] = end is { } e ? Iso(e) : null,
            ["headcount"] = headcount,
            ["fulfilment"] = FulfilmentMode.InstitutionPrepares,
            ["inMemoryOf"] = " Appachchi ",
        };

    private static List<int> Ints(params int[] values) => [.. values];

    private static Dictionary<string, object?> With(Dictionary<string, object?> body, string field, object? value)
    {
        body[field] = value;
        return body;
    }

    private sealed record Created(Guid PublicId, string Version, int Id, JsonElement[] Days);

    private async Task<Created> CreateAsync(Dictionary<string, object?> body)
    {
        var response = await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await BodyOf(response);
        var publicId = created.GetProperty("publicId").GetGuid();
        var id = await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @publicId;", new { publicId });
        return new Created(publicId, created.GetProperty("version").GetString()!, id, [.. created.GetProperty("days").EnumerateArray()]);
    }

    private sealed class BookingRow
    {
        public string BookingNo { get; init; } = null!;
        public string Source { get; init; } = null!;
        public string Status { get; init; } = null!;
        public Guid? DonorUserId { get; init; }
        public int? InstitutionDonorId { get; init; }
        public string DonorPhone { get; init; } = null!;
        public short Headcount { get; init; }
        public string FulfilmentMode { get; init; } = null!;
        public string? InMemoryOf { get; init; }
        public Guid CreatedBy { get; init; }
        public Guid? CancelledBy { get; init; }
        public string? CancelReason { get; init; }
        public DateTime SlotDate { get; init; }

        public DateOnly Date => DateOnly.FromDateTime(SlotDate);
    }

    private async Task<List<BookingRow>> BookingsOfAsync(int sponsorshipId)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            """
            SELECT b.BookingNo, b.Source, b.Status, b.DonorUserId, b.InstitutionDonorId, b.DonorPhone, b.Headcount,
                   b.FulfilmentMode, b.InMemoryOf, b.CreatedBy, b.CancelledBy, b.CancelReason, si.SlotDate
            FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId
            WHERE b.StandingSponsorshipId = @sponsorshipId ORDER BY si.SlotDate, b.Id;
            """,
            new { sponsorshipId })];
    }

    private Task<int> BookedSponsorsAsync(int slotId, DateOnly date) =>
        ScalarAsync<int>(
            "SELECT ISNULL(MAX(BookedSponsors), 0) FROM booking.SlotInstances WHERE MealSlotId = @slotId AND SlotDate = @date;",
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) });

    private sealed record DayChangeRow(string Kind, long? BookingId, string? Reason, Guid CreatedBy);

    private async Task<DayChangeRow?> DayChangeAsync(int sponsorshipId, DateOnly date)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DayChangeRow>(
            "SELECT Kind, BookingId, Reason, CreatedBy FROM booking.StandingExceptions WHERE StandingSponsorshipId = @sponsorshipId AND ExceptionDate = @date;",
            new { sponsorshipId, date = date.ToDateTime(TimeOnly.MinValue) });
    }

    private static string SkipPath(Guid publicId) => $"{Sponsorships}/{publicId}/skip";

    private static string EndPath(Guid publicId) => $"{Sponsorships}/{publicId}/end";

    // ------------------------------------------------------------------ setting one up (K2, K3, K9)

    [Fact]
    public async Task SettingOneUp_BooksEachDayOfTheWindow_ApprovedForTheDonor()
    {
        var phone = RandomPhone();
        var donor = await DonorAsync(phone: phone);
        var (slotId, slot) = await SlotAsync();
        var first = Today.AddDays(2);

        var created = await CreateAsync(Body(donor, slot, StandingPattern.Daily, first, first.AddDays(4)));

        var bookings = await BookingsOfAsync(created.Id);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => first.AddDays(i)), bookings.Select(b => b.Date));
        var donorId = await ScalarAsync<int>("SELECT Id FROM org.InstitutionDonors WHERE PublicId = @donor;", new { donor });
        Assert.All(bookings, b =>
        {
            // Approved although the slot needs approval: the institution made the arrangement (K3).
            Assert.Equal((Booking.StandingSource, BookingStatus.Approved), (b.Source, b.Status));
            Assert.Equal((donorId, (Guid?)null, phone), (b.InstitutionDonorId!.Value, b.DonorUserId, b.DonorPhone));
            Assert.Equal((25, FulfilmentMode.InstitutionPrepares, "Appachchi"), (b.Headcount, b.FulfilmentMode, b.InMemoryOf));
            Assert.Equal(_alphaOwner, b.CreatedBy);
        });

        // Each day holds its place (Q5).
        Assert.Equal(1, await BookedSponsorsAsync(slotId, first));

        // The answer names each day and its booking.
        Assert.Equal(bookings.Select(b => Iso(b.Date)), created.Days.Select(d => d.GetProperty("date").GetString()));
        Assert.All(created.Days, d => Assert.Equal("booked", d.GetProperty("outcome").GetString()));
        Assert.Equal(bookings.Select(b => b.BookingNo), created.Days.Select(d => d.GetProperty("bookingNo").GetString()));

        // No text per day: the booking events a donor's booking writes are not written (K10's texts come later).
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE AggregateId IN @nos AND ProcessedAt IS NULL;",
            new { nos = bookings.Select(b => b.BookingNo).ToList() }));
        Assert.True(await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'AvailabilityChanged' AND ProcessedAt IS NULL;", new { }) >= 5);
    }

    [Fact]
    public async Task ALongWindow_IsBookedAMonthAhead_AndTheJobIsStartedForTheRest()
    {
        var (_, slot) = await SlotAsync(maxAdvance: 40);
        var first = Today.AddDays(2);

        var response = await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, Body(await DonorAsync(), slot, StandingPattern.Daily, first));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        var id = await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @p;", new { p = body.GetProperty("publicId").GetGuid() });

        // Today + 2 to today + 31 while staff wait; today + 32 to today + 40 for the job, started once.
        Assert.Equal(30, body.GetProperty("days").GetArrayLength());
        Assert.Equal(9, body.GetProperty("later").GetInt32());
        Assert.Equal(Today.AddDays(StandingSponsorshipHandler.BookedWhileWaitingDays), (await BookingsOfAsync(id)).Max(b => b.Date));
        Assert.Equal(1, _wake.Wakes);

        Assert.Equal((9, 9), await RunAtAsync(Today) is var run ? (run.Due, run.Booked) : default);
        Assert.Equal(Today.AddDays(40), (await BookingsOfAsync(id)).Max(b => b.Date));

        // A window inside the month leaves the job nothing, and does not start it.
        var (_, shortSlot) = await SlotAsync(maxAdvance: 31);
        var shortOne = await BodyOf(await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, Body(await DonorAsync(), shortSlot, StandingPattern.Daily, first)));
        Assert.Equal(0, shortOne.GetProperty("later").GetInt32());
        Assert.Equal(1, _wake.Wakes);
    }

    [Fact]
    public async Task ALinkedDonorsDays_AreTheirOwnBookings()
    {
        var phone = RandomPhone();
        var account = await UserAsync("Donor", phone);
        var donor = await DonorAsync(userId: account, phone: phone);
        var (_, slot) = await SlotAsync();

        var created = await CreateAsync(Body(donor, slot, StandingPattern.Once, Today.AddDays(3)));

        var booking = Assert.Single(await BookingsOfAsync(created.Id));
        Assert.Equal(account, booking.DonorUserId);

        // Under their own session, through the bookings' owner branch (K16).
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'UserId', @value = @account, @read_only = 1;", new { account });
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.Bookings WHERE BookingNo = @no;", new { no = booking.BookingNo }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.StandingSponsorships;"));
    }

    [Fact]
    public async Task AMonthlyPatternWithTwoDays_BooksExactlyThoseDays()
    {
        var donor = await DonorAsync();
        var (_, slot) = await SlotAsync(maxAdvance: 70);
        var start = Today.AddDays(2);
        var a = Today.AddDays(5).Day;
        var b = Today.AddDays(40).Day;

        var created = await CreateAsync(With(Body(donor, slot, StandingPattern.Monthly, start), "monthDays", Ints(b, a)));

        // The first month while staff wait, the rest by the job.
        await RunAtAsync(Today);

        // Worked out here day by day, not by the pattern code.
        var expected = new List<DateOnly>();
        for (var day = start; day <= Today.AddDays(70); day = day.AddDays(1))
        {
            if (day.Day == a || day.Day == b)
            {
                expected.Add(day);
            }
        }

        Assert.Equal(expected, (await BookingsOfAsync(created.Id)).Select(x => x.Date));
    }

    [Fact]
    public async Task Clashes_AreListedWithTheirReason_AndTheOtherDaysBooked()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);
        var first = Today.AddDays(2);

        // Another regular donor holds the second day; the institution has another use for the third.
        await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, first.AddDays(1)));
        await ExecuteAsync(
            "INSERT booking.BlockedDates (InstitutionId, DateFrom, DateTo, Reason, CreatedBy) VALUES (@alpha, @day, @day, N'Kathina', @by);",
            new { alpha = database.AlphaInstitutionId, day = first.AddDays(2).ToDateTime(TimeOnly.MinValue), by = SethikaTestDatabase.AlphaUserId });

        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Daily, first, first.AddDays(3)));

        Assert.Equal(
            [("booked", null), ("notBooked", BookingErrors.SlotFull), ("notBooked", BookingErrors.DateBlocked), ("booked", null)],
            created.Days.Select(d => (d.GetProperty("outcome").GetString(), d.GetProperty("error").GetString())).ToArray());
        Assert.Equal([first, first.AddDays(3)], (await BookingsOfAsync(created.Id)).Select(b => b.Date));
        Assert.Equal(1, await BookedSponsorsAsync(slotId, first.AddDays(1)));
    }

    [Fact]
    public async Task ADayTheDonorAlreadyHolds_IsNotBookedTwice_NorCalledFull()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);
        var donor = await DonorAsync();
        var first = Today.AddDays(2);
        await CreateAsync(Body(donor, slot, StandingPattern.Once, first.AddDays(1)));

        // The same person's second sponsorship meets their own day: theirs already, not taken by someone else.
        var created = await CreateAsync(Body(donor, slot, StandingPattern.Daily, first, first.AddDays(2)));

        Assert.Equal(["booked", "alreadyHeld", "booked"], created.Days.Select(d => d.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal([first, first.AddDays(2)], (await BookingsOfAsync(created.Id)).Select(b => b.Date));
        Assert.Equal(1, await BookedSponsorsAsync(slotId, first.AddDays(1)));
    }

    [Fact]
    public async Task TheFormIsRefusedFieldByField_AndNothingIsSaved()
    {
        var donor = await DonorAsync();
        var (_, slot) = await SlotAsync();
        var (_, betaSlot) = await SlotAsync(institutionId: database.BetaInstitutionId);
        var betaDonor = await DonorAsync(institutionId: database.BetaInstitutionId);
        var start = Today.AddDays(3);

        async Task Refused(Dictionary<string, object?> body, params string[] fields) =>
            await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, body), fields);

        await Refused(Body(donor, slot, "Fortnightly", start), "pattern");
        await Refused(Body(donor, slot, StandingPattern.Weekly, start), "weekDays");
        await Refused(With(Body(donor, slot, StandingPattern.Weekly, start), "weekDays", Ints(7)), "weekDays");
        await Refused(With(Body(donor, slot, StandingPattern.Daily, start), "monthDays", Ints(5)), "monthDays");
        await Refused(With(Body(donor, slot, StandingPattern.Daily, start), "monthInterval", 2), "monthInterval");
        await Refused(With(With(Body(donor, slot, StandingPattern.Monthly, start), "monthDays", Ints(5)), "monthInterval", 13), "monthInterval");
        await Refused(With(With(Body(donor, slot, StandingPattern.Yearly, start), "yearMonth", 4), "yearDay", 31), "yearDay");
        await Refused(With(With(Body(donor, slot, StandingPattern.Yearly, start), "yearMonth", 13), "yearDay", 1), "yearMonth");
        await Refused(With(Body(donor, slot, StandingPattern.Weekly, start), "yearDay", 3), "weekDays", "yearDay");
        await Refused(Body(donor, slot, StandingPattern.Daily, Today.AddDays(-1)), "startDate");
        await Refused(Body(donor, slot, StandingPattern.Daily, start, start.AddDays(-1)), "endDate");
        await Refused(Body(donor, slot, StandingPattern.Once, start, start.AddDays(1)), "endDate");
        await Refused(Body(donor, slot, StandingPattern.Daily, start, headcount: 201), "headcount");
        await Refused(With(Body(donor, slot, StandingPattern.Daily, start), "fulfilment", FulfilmentMode.Caterer), "fulfilment");
        await Refused(With(Body(donor, slot, StandingPattern.Daily, start), "inMemoryOf", new string('x', 201)), "inMemoryOf");

        // Another institution's donor or slot is not one to name, nor a donor or slot no longer in use.
        await Refused(Body(betaDonor, slot, StandingPattern.Daily, start), "donor");
        await Refused(Body(donor, betaSlot, StandingPattern.Daily, start), "mealSlot");
        var gone = await DonorAsync();
        var (_, closed) = await SlotAsync();
        await ExecuteAsync(
            "UPDATE org.InstitutionDonors SET IsActive = 0 WHERE PublicId = @gone; UPDATE booking.MealSlots SET IsActive = 0 WHERE PublicId = @closed;",
            new { gone, closed });
        await Refused(Body(gone, slot, StandingPattern.Daily, start), "donor");
        await Refused(Body(donor, closed, StandingPattern.Daily, start), "mealSlot");

        // The 31st every twelve months, from a month that has no 31st: a sponsorship of no days.
        var april = new DateOnly(Today.Year + 1, 4, 1);
        await Refused(With(With(Body(donor, slot, StandingPattern.Monthly, april), "monthDays", Ints(31)), "monthInterval", 12), "pattern");

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.StandingSponsorships;", new { }));
    }

    // ------------------------------------------------------------------ the nightly job (K3)

    [Fact]
    public async Task TheJobBooksEachDayAsTheWindowReachesIt_AndARerunBooksNothing()
    {
        var donor = await DonorAsync();
        var (_, slot) = await SlotAsync(maxAdvance: 10);
        var first = Today.AddDays(2);

        var created = await CreateAsync(Body(donor, slot, StandingPattern.Daily, first, first.AddDays(19)));
        Assert.Equal(first.AddDays(8), (await BookingsOfAsync(created.Id)).Max(b => b.Date));

        // Five nights later the window reaches five more days.
        var run = await RunAtAsync(Today.AddDays(5));
        Assert.Equal((1, 5, 5, 0, 0, 0), (run.Sponsorships, run.Due, run.Booked, run.NotBooked, run.Skipped, run.Busy));
        Assert.Equal(Enumerable.Range(0, 14).Select(i => first.AddDays(i)), (await BookingsOfAsync(created.Id)).Select(b => b.Date));

        var again = await RunAtAsync(Today.AddDays(5));
        Assert.Equal((0, 0), (again.Due, again.Booked));
        Assert.Equal(14, (await BookingsOfAsync(created.Id)).Count);
    }

    [Fact]
    public async Task ADayCancelledOnTheCalendar_IsNeverBookedAgain()
    {
        var donor = await DonorAsync();
        var (_, slot) = await SlotAsync();
        var day = Today.AddDays(4);
        var created = await CreateAsync(Body(donor, slot, StandingPattern.Once, day));
        var booking = Assert.Single(await BookingsOfAsync(created.Id));

        var cancelled = await SendAsync(Alpha(), HttpMethod.Post, $"/api/institution/bookings/{booking.BookingNo}/cancel", new { reason = "Temple function" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var run = await RunAtAsync(Today);
        Assert.Equal((0, 0), (run.Due, run.Booked));

        // And the engine's own check, without the run's shortcut in front of it.
        var result = await BookDayAtAsync(created.Id, day, Today);
        Assert.Equal(StandingDayOutcome.AlreadyDone, result.Outcome);
        Assert.Equal(BookingStatus.Cancelled, Assert.Single(await BookingsOfAsync(created.Id)).Status);
    }

    [Fact]
    public async Task ADayOpensLater_TheJobBooksItThen()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);
        var day = Today.AddDays(5);
        var other = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, day));
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, day));
        Assert.Equal(BookingErrors.SlotFull, Assert.Single(created.Days).GetProperty("error").GetString());

        // The first sponsor cannot give: the place opens, and the night's run gives it to the second.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, SkipPath(other.PublicId), new { date = Iso(day) })).StatusCode);
        var run = await RunAtAsync(Today.AddDays(1));

        Assert.Equal((1, 1), (run.Due, run.Booked));
        Assert.Equal(BookingStatus.Approved, Assert.Single(await BookingsOfAsync(created.Id)).Status);
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));
    }

    // ------------------------------------------------------------------ skipping a day (K4, K5)

    [Fact]
    public async Task ASkippedDay_IsLetGo_ThePlaceOpens_AndItIsNeverBookedAgain()
    {
        var (slotId, slot) = await SlotAsync();
        var first = Today.AddDays(2);
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Daily, first, first.AddDays(2)));
        var day = first.AddDays(1);

        var response = await SendAsync(Alpha(), HttpMethod.Post, SkipPath(created.PublicId), new { date = Iso(day), reason = "  Away in Kandy  " });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var bookings = await BookingsOfAsync(created.Id);
        var skipped = Assert.Single(bookings, b => b.Date == day);
        Assert.Equal((BookingStatus.Cancelled, (Guid?)_alphaOwner, "Away in Kandy"), (skipped.Status, skipped.CancelledBy, skipped.CancelReason));
        Assert.All(bookings.Where(b => b.Date != day), b => Assert.Equal(BookingStatus.Approved, b.Status));
        Assert.Equal(0, await BookedSponsorsAsync(slotId, day));

        var change = await DayChangeAsync(created.Id, day);
        var bookingId = await ScalarAsync<long>("SELECT Id FROM booking.Bookings WHERE BookingNo = @no;", new { no = skipped.BookingNo });
        Assert.Equal(new DayChangeRow(StandingDayChange.Skipped, bookingId, "Away in Kandy", _alphaOwner), change);

        // The run passes it over, and the engine refuses it too.
        Assert.Equal(0, (await RunAtAsync(Today)).Due);
        Assert.Equal(StandingDayOutcome.AlreadyDone, (await BookDayAtAsync(created.Id, day, Today)).Outcome);
        Assert.Single(await BookingsOfAsync(created.Id), b => b.Date == day);
    }

    [Fact]
    public async Task ADayBeyondTheWindow_CanBeSkippedAhead()
    {
        var (_, slot) = await SlotAsync(maxAdvance: 10);
        var first = Today.AddDays(2);
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Daily, first, first.AddDays(19)));
        var ahead = first.AddDays(15);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, SkipPath(created.PublicId), new { date = Iso(ahead) })).StatusCode);
        Assert.Equal(new DayChangeRow(StandingDayChange.Skipped, null, null, _alphaOwner), await DayChangeAsync(created.Id, ahead));

        await RunAtAsync(Today.AddDays(10));

        var dates = (await BookingsOfAsync(created.Id)).Select(b => b.Date).ToList();
        Assert.DoesNotContain(ahead, dates);
        Assert.Contains(ahead.AddDays(-1), dates);
        Assert.Contains(ahead.AddDays(1), dates);
    }

    [Fact]
    public async Task ASkipIsRefused_ForADayItCannotLetGo()
    {
        var (_, slot) = await SlotAsync();
        var first = Today.AddDays(2);
        var weekly = await CreateAsync(With(Body(await DonorAsync(), slot, StandingPattern.Weekly, first), "weekDays", Ints((int)first.DayOfWeek)));

        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first.AddDays(1)) }), "date");
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first.AddDays(-7)) }), "date");

        // A day of the pattern that has passed: given or not, it is history.
        var running = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Daily, first));
        await ExecuteAsync("UPDATE booking.StandingSponsorships SET StartDate = DATEADD(DAY, -14, StartDate) WHERE Id = @id;", new { id = running.Id });
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, SkipPath(running.PublicId), new { date = Iso(Today.AddDays(-1)) }), "date");
        Assert.Null(await DayChangeAsync(running.Id, Today.AddDays(-1)));
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { }), "date");
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first), reason = new string('x', 301) }), "reason");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first) })).StatusCode);
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first) }),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.DayAlreadyChanged);

        // Another institution's reads as none; staff may look but not change (K11).
        await AssertRefusedAsync(
            await SendAsync(Beta(), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first.AddDays(7)) }),
            HttpStatusCode.NotFound,
            SponsorshipChangeResult.NotFound);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, SkipPath(weekly.PublicId), new { date = Iso(first.AddDays(7)) })).StatusCode);
        Assert.Null(await DayChangeAsync(weekly.Id, first.AddDays(7)));
    }

    // ------------------------------------------------------------------ ending (K2)

    [Fact]
    public async Task Ending_LetsGoEveryDayAfterToday_KeepsToday_AndBooksNothingMore()
    {
        var (slotId, slot) = await SlotAsync(maxAdvance: 10);
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Daily, Today));

        // Today is too soon for the slot when it is set up; booked the night before, it is today's meal.
        Assert.Equal(StandingDayOutcome.Booked, (await BookDayAtAsync(created.Id, Today, Today.AddDays(-1))).Outcome);
        Assert.Equal(11, (await BookingsOfAsync(created.Id)).Count);

        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, EndPath(created.PublicId), new { version = Convert.ToBase64String(new byte[8]) }),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.Stale);
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, EndPath(created.PublicId), new { }), "version");

        var ended = await SendAsync(Alpha(), HttpMethod.Post, EndPath(created.PublicId), new { version = created.Version });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);

        var bookings = await BookingsOfAsync(created.Id);
        Assert.Equal(BookingStatus.Approved, Assert.Single(bookings, b => b.Date == Today).Status);
        Assert.All(bookings.Where(b => b.Date > Today), b =>
            Assert.Equal((BookingStatus.Cancelled, StandingSponsorshipHandler.EndedReason, (Guid?)_alphaOwner), (b.Status, b.CancelReason, b.CancelledBy)));
        Assert.Equal(0, await BookedSponsorsAsync(slotId, Today.AddDays(5)));
        Assert.Equal(1, await BookedSponsorsAsync(slotId, Today));

        Assert.Equal(StandingSponsorship.EndedStatus, await ScalarAsync<string>("SELECT Status FROM booking.StandingSponsorships WHERE Id = @id;", new { id = created.Id }));
        Assert.Equal(_alphaOwner, await ScalarAsync<Guid>("SELECT EndedBy FROM booking.StandingSponsorships WHERE Id = @id;", new { id = created.Id }));

        var run = await RunAtAsync(Today.AddDays(3));
        Assert.Equal((0, 0, 0), (run.Sponsorships, run.Due, run.Booked));
        Assert.Equal(StandingDayOutcome.Ended, (await BookDayAtAsync(created.Id, Today.AddDays(5), Today)).Outcome);

        var version = (await BodyOf(ended)).GetProperty("version").GetString();
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, EndPath(created.PublicId), new { version }),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.Ended);
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, SkipPath(created.PublicId), new { date = Iso(Today.AddDays(6)) }),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.Ended);
    }

    // ------------------------------------------------------------------ reading (K2, K9)

    [Fact]
    public async Task OneReadsWithItsNextDays()
    {
        // Today + 2 cancelled on the calendar, + 3 skipped, + 4 booked, + 5 (the window's last day)
        // held by someone else, + 6 on beyond the window.
        var (_, slot) = await SlotAsync(maxAdvance: 5, maxSponsors: 1);
        var first = Today.AddDays(2);
        await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, first.AddDays(3)));
        var donor = await DonorAsync();
        var created = await CreateAsync(Body(donor, slot, StandingPattern.Daily, first));
        var firstNo = (await BookingsOfAsync(created.Id))[0].BookingNo;
        await SendAsync(Alpha(), HttpMethod.Post, $"/api/institution/bookings/{firstNo}/cancel", new { reason = "Temple function" });
        await SendAsync(Alpha(), HttpMethod.Post, SkipPath(created.PublicId), new { date = Iso(first.AddDays(1)), reason = "Away" });

        // Staff may read (VIEW).
        var response = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Sponsorships}/{created.PublicId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);

        var sponsorship = body.GetProperty("sponsorship");
        Assert.Equal((donor, StandingPattern.Daily, "Active"), (sponsorship.GetProperty("donor").GetGuid(), sponsorship.GetProperty("pattern").GetString(), sponsorship.GetProperty("status").GetString()));
        Assert.Equal("Sunil Perera", sponsorship.GetProperty("donorName").GetString());

        var days = body.GetProperty("days").EnumerateArray().ToArray();
        Assert.Equal(StandingSponsorshipReader.NextDayCount, days.Length);
        Assert.Equal(
            ["cancelled", "Skipped", "booked", "open", "upcoming", "upcoming"],
            days.Take(6).Select(d => d.GetProperty("state").GetString()!).ToArray());
        Assert.Equal(Iso(first), days[0].GetProperty("date").GetString());
        Assert.Equal("Away", days[1].GetProperty("reason").GetString());
        Assert.Equal(BookingStatus.Cancelled, days[1].GetProperty("bookingStatus").GetString());
        Assert.Equal((firstNo, BookingStatus.Cancelled), (days[0].GetProperty("bookingNo").GetString(), days[0].GetProperty("bookingStatus").GetString()));
        Assert.Equal(BookingStatus.Approved, days[2].GetProperty("bookingStatus").GetString());
    }

    // ------------------------------------------------------------------ the form's preview (K9) and the screens' markers

    private const string Preview = Sponsorships + "/preview";

    [Fact]
    public async Task ThePreview_LaysOutTheNextDays_WithEachClash_AndWritesNothing()
    {
        // A 5-day window, one sponsor a day: + 3 held by another regular donor, + 4 blocked, + 6 on beyond the window.
        var (_, slot) = await SlotAsync(maxAdvance: 5, maxSponsors: 1);
        await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, Today.AddDays(3)));
        await ExecuteAsync(
            "INSERT booking.BlockedDates (InstitutionId, DateFrom, DateTo, Reason, CreatedBy) VALUES (@alpha, @day, @day, N'Kathina', @by);",
            new { alpha = database.AlphaInstitutionId, day = Today.AddDays(4).ToDateTime(TimeOnly.MinValue), by = SethikaTestDatabase.AlphaUserId });
        var sponsorships = await ScalarAsync<int>("SELECT COUNT(*) FROM booking.StandingSponsorships;", new { });
        var bookings = await ScalarAsync<int>("SELECT COUNT(*) FROM booking.Bookings;", new { });

        // Only the slot and the pattern are read: no donor, no meal.
        var response = await SendAsync(Alpha(), HttpMethod.Post, Preview, new { mealSlot = slot, pattern = StandingPattern.Daily, startDate = Iso(Today) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var days = (await BodyOf(response)).GetProperty("days").EnumerateArray().ToArray();
        Assert.Equal(StandingSponsorshipReader.NextDayCount, days.Length);
        Assert.Equal(
            [
                (Iso(Today), "clash", BookingErrors.TooSoon),
                (Iso(Today.AddDays(1)), "free", null),
                (Iso(Today.AddDays(2)), "free", null),
                (Iso(Today.AddDays(3)), "clash", BookingErrors.Held),
                (Iso(Today.AddDays(4)), "clash", BookingErrors.DateBlocked),
                (Iso(Today.AddDays(5)), "free", null),
                (Iso(Today.AddDays(6)), "later", null),
            ],
            days.Take(7).Select(d => (d.GetProperty("date").GetString(), d.GetProperty("state").GetString(), d.GetProperty("reason").GetString())).ToArray());
        Assert.All(days.Skip(7), d => Assert.Equal("later", d.GetProperty("state").GetString()));

        Assert.Equal(sponsorships, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.StandingSponsorships;", new { }));
        Assert.Equal(bookings, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.Bookings;", new { }));
    }

    [Fact]
    public async Task ThePreview_FollowsThePattern_AndSkipsDaysAMonthDoesNotHave()
    {
        var (_, slot) = await SlotAsync(maxAdvance: 365);

        // The 31st every other month from a 31-day month: the months between are skipped, and so is any
        // 31-day-less month that falls on the pattern.
        var start = new DateOnly(Today.Year + 1, 1, 1);
        var response = await SendAsync(Alpha(), HttpMethod.Post, Preview, new
        {
            mealSlot = slot,
            pattern = StandingPattern.Monthly,
            monthInterval = 2,
            monthDays = Ints(31),
            startDate = Iso(start),
            endDate = Iso(start.AddYears(2)),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dates = (await BodyOf(response)).GetProperty("days").EnumerateArray().Select(d => d.GetProperty("date").GetString()!).ToArray();

        // January, March, May, July; September and November have 30 days. Then the same next year.
        var y = start.Year;
        Assert.Equal(
            [$"{y}-01-31", $"{y}-03-31", $"{y}-05-31", $"{y}-07-31", $"{y + 1}-01-31", $"{y + 1}-03-31", $"{y + 1}-05-31", $"{y + 1}-07-31"],
            dates);
    }

    [Fact]
    public async Task ThePreview_IsRefusedFieldByField_ForAnotherInstitutionsSlot_AndForStaff()
    {
        var (_, slot) = await SlotAsync();
        var (_, betaSlot) = await SlotAsync(institutionId: database.BetaInstitutionId);

        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, Preview, new { mealSlot = slot, pattern = StandingPattern.Weekly, startDate = Iso(Today) }), "weekDays");
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, Preview, new { mealSlot = slot, pattern = StandingPattern.Daily, startDate = Iso(Today.AddDays(-1)) }), "startDate");
        await AssertFieldsAsync(await SendAsync(Alpha(), HttpMethod.Post, Preview, new { mealSlot = betaSlot, pattern = StandingPattern.Daily, startDate = Iso(Today) }), "mealSlot");

        // Previewing is part of setting one up (CREATE): staff read only (K11).
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Preview, new { mealSlot = slot, pattern = StandingPattern.Daily, startDate = Iso(Today) })).StatusCode);
    }

    [Fact]
    public async Task AStandingDay_IsMarkedOnTheCalendar_AndInTheLinkedDonorsBookings()
    {
        var phone = RandomPhone();
        var account = await UserAsync("Donor", phone);
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @account, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;",
            new { account });
        var donor = await DonorAsync(userId: account, phone: phone);
        var (_, slot) = await SlotAsync();
        var day = Today.AddDays(3);
        var created = await CreateAsync(Body(donor, slot, StandingPattern.Once, day));

        // An ordinary booking on the same day: the account's own, made by the donor.
        var ordinary = await SendAsync(ClientFor(account, null), HttpMethod.Post, "/api/bookings", new
        {
            mealSlot = slot,
            date = Iso(day.AddDays(1)),
            headcount = 10,
            fulfilment = FulfilmentMode.DonorBrings,
        });
        Assert.Equal(HttpStatusCode.Created, ordinary.StatusCode);
        var ordinaryNo = (await BodyOf(ordinary)).GetProperty("bookingNo").GetString();

        var calendar = (await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"/api/institution/bookings?from={Iso(day)}&to={Iso(day.AddDays(1))}")))
            .EnumerateArray()
            .Where(d => d.GetProperty("mealSlot").GetGuid() == slot)
            .SelectMany(d => d.GetProperty("bookings").EnumerateArray())
            .ToDictionary(b => b.GetProperty("bookingNo").GetString()!, b => b.GetProperty("standing"));
        var standingNo = (await BookingsOfAsync(created.Id))[0].BookingNo;
        Assert.Equal(created.PublicId, calendar[standingNo].GetGuid());
        Assert.Equal(JsonValueKind.Null, calendar[ordinaryNo!].ValueKind);

        var mine = (await BodyOf(await SendAsync(ClientFor(account, null), HttpMethod.Get, "/api/bookings/mine")))
            .EnumerateArray()
            .ToDictionary(b => b.GetProperty("bookingNo").GetString()!, b => b.GetProperty("regular").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { [standingNo] = true, [ordinaryNo!] = false }, mine);
    }

    [Fact]
    public async Task OneInstitutionNeverSeesAnothersSponsorships()
    {
        var (_, slot) = await SlotAsync();
        var donor = await DonorAsync();
        var created = await CreateAsync(Body(donor, slot, StandingPattern.Once, Today.AddDays(3)));

        await AssertRefusedAsync(await SendAsync(Beta(), HttpMethod.Get, $"{Sponsorships}/{created.PublicId}"), HttpStatusCode.NotFound, SponsorshipChangeResult.NotFound);
        Assert.Empty((await BodyOf(await SendAsync(Beta(), HttpMethod.Get, Sponsorships))).EnumerateArray());
        await AssertRefusedAsync(
            await SendAsync(Beta(), HttpMethod.Post, EndPath(created.PublicId), new { version = created.Version }),
            HttpStatusCode.NotFound,
            SponsorshipChangeResult.NotFound);

        var mine = (await BodyOf(await SendAsync(Alpha(), HttpMethod.Get, $"{Sponsorships}?donor={donor}"))).EnumerateArray().ToArray();
        Assert.Equal(created.PublicId, Assert.Single(mine).GetProperty("publicId").GetGuid());
        Assert.Empty((await BodyOf(await SendAsync(Alpha(), HttpMethod.Get, $"{Sponsorships}?donor={Guid.CreateVersion7()}"))).EnumerateArray());

        // Staff may read but not set one up (K11).
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Sponsorships, Body(donor, slot, StandingPattern.Once, Today.AddDays(4)))).StatusCode);
    }

    // ------------------------------------------------------------------ the races, forced (K4)

    /// <summary>
    /// Holds an update lock on the sponsorship's row from another connection, starts the engine on
    /// <paramref name="day"/>, waits until it is queued on the row, then runs <paramref name="change"/>
    /// in the holding transaction and commits: what the engine does next is what it saw after the change.
    /// The engine runs four days before <paramref name="day"/>, inside the 5-day window of these slots.
    /// </summary>
    private async Task<StandingDayResult> RaceAsync(int sponsorshipId, DateOnly day, string change)
    {
        await using var holder = await PlatformConnectionAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM booking.StandingSponsorships WITH (UPDLOCK, ROWLOCK) WHERE Id = @sponsorshipId;",
            new { sponsorshipId },
            transaction);

        var booking = BookDayAtAsync(sponsorshipId, day, day.AddDays(-4));

        await using (var observer = await PlatformConnectionAsync())
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await observer.ExecuteScalarAsync<int>(
                       """
                       SELECT COUNT(*) FROM sys.dm_tran_locks l
                       JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                       WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID()
                         AND p.object_id = OBJECT_ID('booking.StandingSponsorships');
                       """) == 0)
            {
                Assert.False(booking.IsCompleted, "The engine did not wait for the sponsorship's row lock.");
                Assert.True(DateTime.UtcNow < deadline, "The race was not forced.");
                await Task.Delay(25);
            }
        }

        await holder.ExecuteAsync(change, new { sponsorshipId, day = day.ToDateTime(TimeOnly.MinValue), by = _alphaOwner, institutionId = database.AlphaInstitutionId }, transaction);
        await transaction.CommitAsync();
        return await booking;
    }

    [Fact]
    public async Task ASkipHoldingTheSponsorship_IsNeverCrossedByTheJob()
    {
        var (slotId, slot) = await SlotAsync(maxAdvance: 5);
        var day = Today.AddDays(8);
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, day));
        Assert.Empty(await BookingsOfAsync(created.Id));

        var result = await RaceAsync(
            created.Id,
            day,
            """
            INSERT booking.StandingExceptions (InstitutionId, StandingSponsorshipId, ExceptionDate, Kind, CreatedBy)
            VALUES (@institutionId, @sponsorshipId, @day, 'Skipped', @by);
            """);

        Assert.Equal(StandingDayOutcome.AlreadyDone, result.Outcome);
        Assert.Empty(await BookingsOfAsync(created.Id));
        Assert.Equal(0, await BookedSponsorsAsync(slotId, day));
    }

    [Fact]
    public async Task AnEndHoldingTheSponsorship_IsNeverCrossedByTheJob()
    {
        var (_, slot) = await SlotAsync(maxAdvance: 5);
        var day = Today.AddDays(8);
        var created = await CreateAsync(Body(await DonorAsync(), slot, StandingPattern.Once, day));

        var result = await RaceAsync(
            created.Id,
            day,
            "UPDATE booking.StandingSponsorships SET Status = 'Ended', EndedAt = SYSUTCDATETIME(), EndedBy = @by WHERE Id = @sponsorshipId;");

        Assert.Equal(StandingDayOutcome.Ended, result.Outcome);
        Assert.Empty(await BookingsOfAsync(created.Id));
    }
}

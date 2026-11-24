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
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Notifications;
using Sethika.Application.Bookings.Recurring;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 07, C.1: yearly bookings against real SQL Server, through the real engine. The yearly job is
/// run on a fixed clock - the day a year enters its slot's window, the day before, a day after it has
/// passed - and the race between two runs is forced, not hoped for. The texts go through the outbox,
/// the seeded templates and the console sender, like every other booking text.
/// </summary>
/// <remarks>
/// Yearly bookings the job works on are written straight into the table, as the platform, so each
/// test states the years it needs; the ones the donor starts are made through the engine and the
/// endpoint. The job's engine runs without the Redis lock (C2 says the database decides); the lock
/// in front of it is the booking engine's own tests' subject.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RecurringBookingTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "recurring-booking-test-signing-key-at-least-32-bytes";
    private const string NameSi = "ඇල්ෆා විහාරය";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    /// <summary>A day 70 days ahead: a yearly booking's next year, with a 60-day window to open on it.</summary>
    private static DateOnly Day => Today.AddDays(70);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;
            UPDATE org.Institutions SET NameSi = @NameSi WHERE Id = @alpha;
            """,
            new { NameSi, alpha = database.AlphaInstitutionId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new LoopbackProxyStartupFilter()));
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
            new { phones = _phones, users = _users });

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

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

    /// <summary>00:30 in Colombo on <paramref name="today"/>, when the job runs.</summary>
    private static FixedTimeProvider ClockAt(DateOnly today) => new(SriLankaTime.ToUtc(today, new TimeOnly(0, 30)));

    private static BookMealHandler Engine(SethikaDbContext context, TimeProvider clock) =>
        new(context, new NoLocks(), new SqlSlotInventory(context), new SqlNumberSeries(context), clock);

    /// <summary>The yearly job as it runs at 00:30 on <paramref name="today"/>, as the platform.</summary>
    private async Task<RecurringRun> RunAtAsync(DateOnly today)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        var clock = ClockAt(today);
        return await new RecurringBookingRun(context, Engine(context, clock), clock).RunAsync();
    }

    private async Task<RecurringYearResult> BookYearAtAsync(int yearly, int year, DateOnly today)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        return await Engine(context, ClockAt(today)).BookYearAsync(yearly, year);
    }

    /// <summary>A donor's own booking through the engine, made on <paramref name="today"/>.</summary>
    private async Task<BookMealResult> BookAsync(Guid donor, Guid slot, DateOnly date, DateOnly today, bool everyYear = false)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        return await Engine(context, ClockAt(today))
            .BookAsync(donor, new BookMealCommand(slot, date, 10, FulfilmentMode.DonorBrings, "Amma", null, everyYear));
    }

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<(Guid Id, string Phone)> DonorAsync(string language = "en")
    {
        var id = Guid.CreateVersion7();
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Yearly Donor', @phone, 1, @language, 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone, language });

        return (id, phone);
    }

    /// <summary>A lunch at Alpha, every day, approval needed, a 1-<paramref name="maxAdvance"/> day window.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync(string mode = "Shared", byte maxSponsors = 10, short maxAdvance = 60)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @alpha, @name, '11:30', '13:00', @mode, @maxSponsors, 1, 200, 1, @maxAdvance, 1,
                    'DonorBrings,InstitutionPrepares', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId,
                alpha = database.AlphaInstitutionId,
                name = "Yearly " + Guid.NewGuid().ToString("N")[..8],
                mode,
                maxSponsors,
                maxAdvance,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return (id, publicId);
    }

    /// <summary>
    /// A yearly booking of <paramref name="donor"/>'s on <paramref name="month"/>/<paramref name="day"/>,
    /// written as the platform: last tried in <paramref name="lastYear"/>, so the job tries the year after.
    /// </summary>
    private async Task<(int Id, Guid PublicId)> YearlyAsync(Guid donor, int slotId, int month, int day, int lastYear)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.RecurringBookings
                (PublicId, DonorUserId, InstitutionId, MealSlotId, RecurMonth, RecurDay, StartYear, LastGeneratedYear,
                 Headcount, FulfilmentMode, InMemoryOf)
            VALUES (@publicId, @donor, @alpha, @slotId, @month, @day, @lastYear + 1, @lastYear, 12, 'InstitutionPrepares', N'Appachchi');
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { publicId, donor, alpha = database.AlphaInstitutionId, slotId, month, day, lastYear });

        return (id, publicId);
    }

    private Task<(int Id, Guid PublicId)> YearlyOnAsync(Guid donor, int slotId, DateOnly date) =>
        YearlyAsync(donor, slotId, date.Month, date.Day, date.Year - 1);

    private sealed record BookingRow(string BookingNo, string Source, string Status, short Headcount, string FulfilmentMode, string? InMemoryOf, Guid DonorUserId, DateTime SlotDate);

    private async Task<List<BookingRow>> BookingsOfAsync(int yearly)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            """
            SELECT b.BookingNo, b.Source, b.Status, b.Headcount, b.FulfilmentMode, b.InMemoryOf, b.DonorUserId, si.SlotDate
            FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId
            WHERE b.RecurringBookingId = @yearly ORDER BY si.SlotDate;
            """,
            new { yearly })];
    }

    private Task<int?> LastYearOfAsync(int yearly) =>
        ScalarAsync<int?>("SELECT LastGeneratedYear FROM booking.RecurringBookings WHERE Id = @yearly;", new { yearly });

    private Task<int> NotMadeMessagesAsync(Guid publicId) =>
        ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'RecurringBookingNotMade' AND AggregateId = @id;",
            new { id = publicId.ToString() });

    private SqlConnectionFactory PlatformConnections() => new(database.ConnectionString, TestCurrentUserContext.Platform());

    private OutboxProcessor Processor()
    {
        var connections = PlatformConnections();
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);
        var queries = new BookingNotificationQueries(connections);
        return new OutboxProcessor(
            connections,
            [new BookingSmsHandler(queries, texts), new RecurringBookingSmsHandler(queries, texts)],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
    }

    private sealed record Sent(string Body, string DedupKey);

    private async Task<List<Sent>> SentToAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<Sent>(
            "SELECT Body AS Body, DedupKey AS DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;",
            new { phone })];
    }

    private async Task<string> ExpectedAsync(string template, string language, DateOnly date, string institution)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var body = await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @template AND Channel = 'SMS' AND Language = @language;",
            new { template, language }) ?? throw new InvalidOperationException($"{template} {language} is not seeded.");

        return body
            .Replace("{institution}", institution, StringComparison.Ordinal)
            .Replace("{date}", Iso(date), StringComparison.Ordinal)
            .Replace("{time}", "11:30", StringComparison.Ordinal);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private HttpClient ClientFor(Guid userId, int? institutionId = null)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.7.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
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

    // ------------------------------------------------------------------ the yearly job

    [Fact]
    public async Task TheJobBooksAYearOnTheDayItsWindowOpens_NotTheDayBefore_ThroughTheEngine()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync(maxAdvance: 60);
        var (yearly, _) = await YearlyOnAsync(donor, slotId, Day);

        var early = await RunAtAsync(Day.AddDays(-61));
        Assert.Equal((0, 0), (early.Due, early.Booked));
        Assert.Empty(await BookingsOfAsync(yearly));
        Assert.Equal(Day.Year - 1, await LastYearOfAsync(yearly));

        var run = await RunAtAsync(Day.AddDays(-60));

        Assert.Equal((Day.AddDays(-60), 1, 1, 0, 0, 0), (run.Today, run.Due, run.Booked, run.NotBooked, run.Skipped, run.Busy));
        var booking = Assert.Single(await BookingsOfAsync(yearly));
        Assert.Equal(
            (Booking.RecurringSource, BookingStatus.Requested, (short)12, FulfilmentMode.InstitutionPrepares, "Appachchi", donor, Day),
            (booking.Source, booking.Status, booking.Headcount, booking.FulfilmentMode, booking.InMemoryOf, booking.DonorUserId, DateOnly.FromDateTime(booking.SlotDate)));
        Assert.Equal(Day.Year, await LastYearOfAsync(yearly));

        // Booked like any other: the place is taken, and the donor's text is on its way.
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT si.BookedSponsors FROM booking.SlotInstances si WHERE si.MealSlotId = @slotId AND si.SlotDate = @day;",
            new { slotId, day = Day.ToDateTime(TimeOnly.MinValue) }));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'BookingRequested' AND AggregateId = @no;", new { no = booking.BookingNo }));
    }

    [Fact]
    public async Task ARunAgain_OrAYearAskedForTwice_BooksNothingTwice()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (yearly, _) = await YearlyOnAsync(donor, slotId, Day);
        Assert.Equal(1, (await RunAtAsync(Day.AddDays(-60))).Booked);

        var again = await RunAtAsync(Day.AddDays(-60));
        var later = await RunAtAsync(Day.AddDays(-30));
        var twice = await BookYearAtAsync(yearly, Day.Year, Day.AddDays(-60));

        Assert.Equal((0, 0), (again.Due, later.Due));
        Assert.Equal(RecurringYearOutcome.AlreadyTried, twice.Outcome);
        Assert.Single(await BookingsOfAsync(yearly));
    }

    [Fact]
    public async Task TwoRunsAtOnceBookTheYearOnce()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (yearly, _) = await YearlyOnAsync(donor, slotId, Day);

        // As the platform: without a session context Row-Level Security hides the row, and the lock locks nothing.
        await using var holder = await PlatformConnectionAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT Id FROM booking.RecurringBookings WITH (UPDLOCK, ROWLOCK) WHERE Id = @yearly;", new { yearly }, hold);

        var runs = Enumerable.Range(0, 2).Select(_ => Task.Run(() => BookYearAtAsync(yearly, Day.Year, Day.AddDays(-60)))).ToArray();

        await using (var observer = new SqlConnection(database.ConnectionString))
        {
            await observer.OpenAsync();
            await WaitForWaitersAsync(observer, "booking.RecurringBookings", 2);
        }

        await hold.CommitAsync();
        var results = await Task.WhenAll(runs);

        Assert.Equal(
            [RecurringYearOutcome.Booked, RecurringYearOutcome.AlreadyTried],
            results.Select(r => r.Outcome).Order().ToArray());
        Assert.Single(await BookingsOfAsync(yearly));
    }

    [Fact]
    public async Task AFullDayBooksNothing_AndTextsTheDonorWhyOnce()
    {
        var (donor, phone) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync(mode: "Exclusive", maxSponsors: 1);
        var (yearly, publicId) = await YearlyOnAsync(donor, slotId, Day);

        // Another donor took the day the moment it opened, before the job reached it.
        Assert.True((await BookAsync(other, slot, Day, today: Day.AddDays(-60))).Succeeded);

        var run = await RunAtAsync(Day.AddDays(-60));

        Assert.Equal((1, 0, 1), (run.Due, run.Booked, run.NotBooked));
        Assert.Empty(await BookingsOfAsync(yearly));
        Assert.Equal(Day.Year, await LastYearOfAsync(yearly));
        Assert.Equal(1, await NotMadeMessagesAsync(publicId));

        await Processor().RunAsync();
        await RunAtAsync(Day.AddDays(-59));
        await Processor().RunAsync();

        var sent = Assert.Single(await SentToAsync(phone));
        Assert.Equal(await ExpectedAsync(RecurringBookingSmsHandler.FullTemplate, "en", Day, "Alpha Temple"), sent.Body);
        Assert.Equal($"{RecurringBookingSmsHandler.FullTemplate}:{publicId}:{Day.Year}", sent.DedupKey);

        // Full again the next year: a text of its own, under that year's key.
        var nextYear = RecurringBooking.OccursIn((byte)Day.Month, (byte)Day.Day, Day.Year + 1);
        Assert.True((await BookAsync(other, slot, nextYear, today: nextYear.AddDays(-60))).Succeeded);
        Assert.Equal(1, (await RunAtAsync(nextYear.AddDays(-60))).NotBooked);
        await Processor().RunAsync();

        Assert.Equal(
            [$"{RecurringBookingSmsHandler.FullTemplate}:{publicId}:{Day.Year}", $"{RecurringBookingSmsHandler.FullTemplate}:{publicId}:{Day.Year + 1}"],
            (await SentToAsync(phone)).Select(s => s.DedupKey));
    }

    [Fact]
    public async Task ADayTheMealIsNotOpen_IsTextedAsSuch_InTheDonorsLanguage()
    {
        var (donor, phone) = await DonorAsync("si");
        var (slotId, _) = await SlotAsync();
        var (yearly, publicId) = await YearlyOnAsync(donor, slotId, Day);
        await ExecuteAsync(
            """
            INSERT booking.BlockedDates (InstitutionId, DateFrom, DateTo, Reason, CreatedBy)
            VALUES (@alpha, @day, @day, N'Festival', @createdBy);
            """,
            new { alpha = database.AlphaInstitutionId, day = Day.ToDateTime(TimeOnly.MinValue), createdBy = SethikaTestDatabase.AlphaUserId });

        var run = await RunAtAsync(Day.AddDays(-60));
        await Processor().RunAsync();

        Assert.Equal((1, 1), (run.Due, run.NotBooked));
        Assert.Empty(await BookingsOfAsync(yearly));
        var sent = Assert.Single(await SentToAsync(phone));
        Assert.Equal(await ExpectedAsync(RecurringBookingSmsHandler.ClosedTemplate, "si", Day, NameSi), sent.Body);
        Assert.Equal($"{RecurringBookingSmsHandler.ClosedTemplate}:{publicId}:{Day.Year}", sent.DedupKey);
    }

    [Fact]
    public async Task The29thOfFebruaryBooksThe28thInACommonYear()
    {
        var year = Today.Year + 1;
        while (DateTime.IsLeapYear(year))
        {
            year++;
        }

        var (donor, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (yearly, _) = await YearlyAsync(donor, slotId, month: 2, day: 29, lastYear: year - 1);
        var day = new DateOnly(year, 2, 28);

        Assert.Equal(0, (await RunAtAsync(day.AddDays(-61))).Due);
        Assert.Equal(1, (await RunAtAsync(day.AddDays(-60))).Booked);

        Assert.Equal(day, DateOnly.FromDateTime(Assert.Single(await BookingsOfAsync(yearly)).SlotDate));
    }

    [Fact]
    public async Task AStoppedYearlyBookingBooksNothingMore_AndTheYearAlreadyBookedStays()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (yearly, publicId) = await YearlyOnAsync(donor, slotId, Day);
        Assert.Equal(1, (await RunAtAsync(Day.AddDays(-60))).Booked);

        // Row-Level Security aside (the platform sees every row), only the donor's own id stops it.
        var (other, _) = await DonorAsync();
        await using (var platform = ContextFor(TestCurrentUserContext.Platform()))
        {
            Assert.Equal(RecurringBookingHandler.NotFound, await new RecurringBookingHandler(platform).StopAsync(publicId, other));
        }

        await using (var context = ContextFor(TestCurrentUserContext.ForUser(donor)))
        {
            Assert.Null(await new RecurringBookingHandler(context).StopAsync(publicId, donor));
        }

        var nextYear = RecurringBooking.OccursIn((byte)Day.Month, (byte)Day.Day, Day.Year + 1);
        Assert.Equal(0, (await RunAtAsync(nextYear.AddDays(-60))).Due);
        Assert.Equal(RecurringYearOutcome.AlreadyTried, (await BookYearAtAsync(yearly, Day.Year + 1, nextYear.AddDays(-60))).Outcome);

        var booked = Assert.Single(await BookingsOfAsync(yearly));
        Assert.Equal((BookingStatus.Requested, Day), (booked.Status, DateOnly.FromDateTime(booked.SlotDate)));
    }

    [Fact]
    public async Task ADonorWhoBookedTheDayThemselvesIsNotBookedTwice_NorTexted()
    {
        var (donor, phone) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var (yearly, publicId) = await YearlyOnAsync(donor, slotId, Day);
        Assert.True((await BookAsync(donor, slot, Day, today: Day.AddDays(-60))).Succeeded);

        var run = await RunAtAsync(Day.AddDays(-60));

        Assert.Equal((1, 0, 0, 1), (run.Due, run.Booked, run.NotBooked, run.Skipped));
        Assert.Empty(await BookingsOfAsync(yearly));
        Assert.Equal(Day.Year, await LastYearOfAsync(yearly));
        Assert.Equal(0, await NotMadeMessagesAsync(publicId));
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId
            WHERE b.DonorPhone = @phone AND si.MealSlotId = @slotId;
            """,
            new { phone, slotId }));
    }

    [Fact]
    public async Task ADonorBookingTheDayAtTheSameMomentWins_AndTheJobGivesBackTheYearAndThePlace()
    {
        var (donor, _) = await DonorAsync();
        var (third, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var (yearly, _) = await YearlyOnAsync(donor, slotId, Day);
        var opens = Day.AddDays(-60);
        Assert.True((await BookAsync(third, slot, Day, opens)).Succeeded);
        var instance = await ScalarAsync<long>(
            "SELECT Id FROM booking.SlotInstances WHERE MealSlotId = @slotId AND SlotDate = @day;",
            new { slotId, day = Day.ToDateTime(TimeOnly.MinValue) });

        // The donor's own booking queues on the slot-day first, then the job, which has already looked
        // for the donor's booking and not found it: the donor wins, and the job's save collides.
        await using var holder = await PlatformConnectionAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT Id FROM booking.SlotInstances WITH (UPDLOCK, ROWLOCK) WHERE Id = @instance;", new { instance }, hold);

        await using var observer = new SqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        var own = Task.Run(() => BookAsync(donor, slot, Day, opens));
        await WaitForWaitersAsync(observer, "booking.SlotInstances", 1);
        var job = Task.Run(() => BookYearAtAsync(yearly, Day.Year, opens));
        await WaitForWaitersAsync(observer, "booking.SlotInstances", 2);
        await hold.CommitAsync();

        Assert.True((await own).Succeeded);
        Assert.Equal(RecurringYearOutcome.Busy, (await job).Outcome);

        // Nothing of the job's attempt stayed: not the year, not the place it took.
        Assert.Equal(Day.Year - 1, await LastYearOfAsync(yearly));
        Assert.Equal(2, await ScalarAsync<int>("SELECT BookedSponsors FROM booking.SlotInstances WHERE Id = @instance;", new { instance }));

        // The next run finds the donor's own booking and leaves the year at that.
        Assert.Equal(RecurringYearOutcome.AlreadyHeld, (await BookYearAtAsync(yearly, Day.Year, opens)).Outcome);
        Assert.Equal(Day.Year, await LastYearOfAsync(yearly));
        Assert.Empty(await BookingsOfAsync(yearly));
    }

    [Fact]
    public async Task AYearWhoseDayHasPassed_OrWhoseDonorHasNoConfirmedPhone_IsClaimedAndLeft()
    {
        var (late, _) = await DonorAsync();
        var (unconfirmed, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (past, pastId) = await YearlyAsync(late, slotId, Today.AddDays(-3).Month, Today.AddDays(-3).Day, Today.AddDays(-3).Year - 1);
        var (noPhone, noPhoneId) = await YearlyOnAsync(unconfirmed, slotId, Day);
        await ExecuteAsync("UPDATE auth.Users SET PhoneConfirmed = 0 WHERE Id = @unconfirmed;", new { unconfirmed });

        var gone = await RunAtAsync(Today);
        var silent = await RunAtAsync(Day.AddDays(-60));

        Assert.Equal((1, 1), (gone.Due, gone.Skipped));
        Assert.Equal((1, 1), (silent.Due, silent.Skipped));
        Assert.Empty(await BookingsOfAsync(past));
        Assert.Empty(await BookingsOfAsync(noPhone));
        Assert.Equal<(int?, int?)>((Today.AddDays(-3).Year, Day.Year), (await LastYearOfAsync(past), await LastYearOfAsync(noPhone)));
        Assert.Equal((0, 0), (await NotMadeMessagesAsync(pastId), await NotMadeMessagesAsync(noPhoneId)));
    }

    [Fact]
    public void TheHostTextsAYearNotBooked_AndRunsTheJobsWorkInItsOwnTypes()
    {
        using var scope = _host.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToList();
        Assert.IsType<RecurringBookingSmsHandler>(Assert.Single(handlers, h => h.EventTypes.Contains(BookMealHandler.YearNotBookedEvent)));

        // A booked year is texted by the booking texts, like any other booking.
        Assert.IsType<BookingSmsHandler>(Assert.Single(handlers, h => h.EventTypes.Contains(BookMealHandler.RequestedEvent)));

        Assert.NotNull(scope.ServiceProvider.GetService<RecurringBookingRun>());
        Assert.NotNull(scope.ServiceProvider.GetService<RecurringBookingHandler>());
    }

    // ------------------------------------------------------------------ starting one

    [Fact]
    public async Task BookingEveryYearStartsAYearlyBookingFromNextYear_OrJoinsTheOneTheDonorHas()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var first = Today.AddDays(20);
        var second = Today.AddDays(21);
        var (existing, _) = await YearlyAsync(donor, slotId, second.Month, second.Day, second.Year);

        var started = await BookAsync(donor, slot, first, Today, everyYear: true);
        var joined = await BookAsync(donor, slot, second, Today, everyYear: true);
        var once = await BookAsync(donor, slot, Today.AddDays(22), Today);

        Assert.True(started.Succeeded && joined.Succeeded && once.Succeeded);

        await using var connection = await PlatformConnectionAsync();
        var rows = (await connection.QueryAsync<(int Id, byte Month, byte Day, short StartYear, short? LastYear, short Headcount, string Fulfilment, string? InMemoryOf)>(
            """
            SELECT Id, RecurMonth, RecurDay, StartYear, LastGeneratedYear, Headcount, FulfilmentMode, InMemoryOf
            FROM booking.RecurringBookings WHERE DonorUserId = @donor ORDER BY Id;
            """,
            new { donor })).ToList();

        Assert.Equal(2, rows.Count);
        var made = rows.Single(r => r.Id != existing);
        Assert.Equal(
            ((byte)first.Month, (byte)first.Day, (short)(first.Year + 1), (short?)first.Year, (short)10, FulfilmentMode.DonorBrings, "Amma"),
            (made.Month, made.Day, made.StartYear, made.LastYear, made.Headcount, made.Fulfilment, made.InMemoryOf));

        var links = (await connection.QueryAsync<(string BookingNo, int? Yearly, string Source)>(
            "SELECT BookingNo, RecurringBookingId, Source FROM booking.Bookings WHERE DonorUserId = @donor;", new { donor }))
            .ToDictionary(b => b.BookingNo);
        Assert.Equal((made.Id, Booking.OnlineSource), (links[started.BookingNo!].Yearly, links[started.BookingNo!].Source));
        Assert.Equal(existing, links[joined.BookingNo!].Yearly);
        Assert.Null(links[once.BookingNo!].Yearly);
    }

    [Fact]
    public async Task OverHttp_TheDonorBooksEveryYear_SeesItListed_AndAloneCanStopIt()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (_, slot) = await SlotAsync();
        var date = Today.AddDays(15);

        string bookingNo;
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(date), headcount = 10, fulfilment = "DonorBrings", inMemoryOf = "Amma", everyYear = true }))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            bookingNo = (await BodyOf(response)).GetProperty("bookingNo").GetString()!;
        }

        Guid publicId;
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/recurring"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var yearly = Assert.Single((await BodyOf(response)).EnumerateArray());
            publicId = yearly.GetProperty("publicId").GetGuid();
            Assert.Equal(
                ("alpha-temple", slot, date.Month, date.Day, Iso(RecurringBooking.OccursIn((byte)date.Month, (byte)date.Day, date.Year + 1)), date.Year, 10, "Amma"),
                (yearly.GetProperty("institution").GetString(), yearly.GetProperty("mealSlot").GetGuid(), yearly.GetProperty("month").GetInt32(),
                 yearly.GetProperty("day").GetInt32(), yearly.GetProperty("nextDate").GetString(), yearly.GetProperty("lastYear").GetInt32(),
                 yearly.GetProperty("headcount").GetInt32(), yearly.GetProperty("inMemoryOf").GetString()));
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/mine"))
        {
            var mine = Assert.Single((await BodyOf(response)).EnumerateArray());
            Assert.Equal((bookingNo, publicId), (mine.GetProperty("bookingNo").GetString(), mine.GetProperty("yearly").GetGuid()));
        }

        // Another donor, or the donor switched into an institution, cannot stop it.
        using (var response = await SendAsync(ClientFor(other), HttpMethod.Post, $"/api/bookings/recurring/{publicId}/stop"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(RecurringBookingHandler.NotFound, (await BodyOf(response)).GetProperty("code").GetString());
        }

        using (var response = await SendAsync(ClientFor(donor, database.AlphaInstitutionId), HttpMethod.Post, $"/api/bookings/recurring/{publicId}/stop"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/recurring/{publicId}/stop"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/recurring/{publicId}/stop"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/recurring"))
        {
            Assert.Empty((await BodyOf(response)).EnumerateArray());
        }

        // Stopped, its bookings stay, and no longer say "every year".
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/mine"))
        {
            var mine = Assert.Single((await BodyOf(response)).EnumerateArray());
            Assert.Equal((bookingNo, JsonValueKind.Null), (mine.GetProperty("bookingNo").GetString(), mine.GetProperty("yearly").ValueKind));
        }
    }

    [Fact]
    public async Task RowLevelSecurityShowsAYearlyBookingToItsDonorAndItsInstitutionOnly()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (slotId, _) = await SlotAsync();
        var (yearly, _) = await YearlyOnAsync(donor, slotId, Day);

        async Task<int> VisibleTo(string key, object value)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = @key, @value = @value, @read_only = 1;", new { key, value });
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.RecurringBookings WHERE Id = @yearly;", new { yearly });
        }

        Assert.Equal(
            (1, 0, 1, 0),
            (await VisibleTo("UserId", donor), await VisibleTo("UserId", other),
             await VisibleTo("InstitutionId", database.AlphaInstitutionId), await VisibleTo("InstitutionId", database.BetaInstitutionId)));

        // Nor can another donor write one for someone else.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'UserId', @value = @other, @read_only = 1;", new { other });
        var refused = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT booking.RecurringBookings (PublicId, DonorUserId, InstitutionId, MealSlotId, RecurMonth, RecurDay, StartYear, Headcount, FulfilmentMode)
            VALUES (NEWID(), @donor, @alpha, @slotId, 1, 1, 2030, 5, 'DonorBrings');
            """,
            new { donor, alpha = database.AlphaInstitutionId, slotId }));
        Assert.Contains("block predicate", refused.Message, StringComparison.OrdinalIgnoreCase);
    }
}

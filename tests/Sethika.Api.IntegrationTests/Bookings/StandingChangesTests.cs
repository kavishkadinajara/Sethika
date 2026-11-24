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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Slots;
using Sethika.Application.Bookings.Sponsors;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 08, K6-K8, K10 and K12: what happens to a standing day after it is booked, against real SQL
/// Server and Redis. A donor asks for a held day and the institution gives it (one transaction: the
/// standing day released, its booking cancelled, the asker booked approved) or declines; a blocked date
/// lets standing days go and lists ordinary bookings; two standing days are swapped this time or from
/// now on; and the texts reach the people they are about, once, and a donor without an account only
/// with consent. The races are forced on the sponsorship's row lock.
/// </summary>
/// <remarks>
/// Alpha's members hold the template roles, cloned as onboarding clones them. Slots need approval and
/// take one sponsor a day unless a test says otherwise, so a standing day fills its day and the public
/// calendar shows it held. A slot's window starts tomorrow (<c>MinAdvanceDays = 1</c>).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class StandingChangesTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "standing-changes-test-signing-key-at-least-32-bytes";
    private const string Sponsorships = "/api/institution/sponsorships";
    private const string Tray = "/api/institution/day-requests";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;
    private Guid _betaOwner;
    private string _alphaSlug = null!;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;

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
            SELECT @beta, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m WHERE m.Code = 'BOOKINGS';
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _alphaOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");
        _alphaSlug = await ScalarAsync<string>("SELECT Slug FROM org.Institutions WHERE Id = @alpha;", new { alpha = database.AlphaInstitutionId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddSingleton<IStandingBookingWake>(new NoWake()));
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

    private sealed class NoWake : IStandingBookingWake
    {
        public void Wake()
        {
        }
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

    private static string RandomPhone() =>
        "+9478" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    private async Task<Guid> UserAsync(string userType, string? phone = null, string language = "en", string? role = null)
    {
        var id = Guid.CreateVersion7();
        phone ??= RandomPhone();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, N'Changes Test', @phone, 1, @language, 'Active', 'stamp');
            IF @role IS NOT NULL
                INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
                SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = @role AND r.InstitutionId IS NULL;
            """,
            new { id, userType, phone, language, role });

        _users.Add(id);
        _phones.Add(phone);
        return id;
    }

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private Task<Guid> AskerAsync(string language = "en") => UserAsync("Donor", language: language, role: "DONOR");

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

    /// <summary>A person in Alpha's donor book; texted only when <paramref name="consent"/> (Q2) or linked to <paramref name="userId"/>.</summary>
    private async Task<(Guid PublicId, string Phone)> DonorAsync(bool consent = true, Guid? userId = null, string? phone = null, string language = "si")
    {
        var publicId = Guid.CreateVersion7();
        phone ??= RandomPhone();
        _phones.Add(phone);
        await ExecuteAsync(
            """
            INSERT org.InstitutionDonors (PublicId, InstitutionId, UserId, FullName, Phone, Language, TextConsentAt, TextConsentBy, CreatedBy)
            VALUES (@publicId, @institutionId, @userId, N'Sunil Perera', @phone, @language,
                    CASE WHEN @consent = 1 THEN SYSUTCDATETIME() END, CASE WHEN @consent = 1 THEN @createdBy END, @createdBy);
            """,
            new { publicId, institutionId = database.AlphaInstitutionId, userId, phone, language, consent, createdBy = SethikaTestDatabase.AlphaUserId });

        return (publicId, phone);
    }

    /// <summary>A lunch at Alpha every day, approval needed, <paramref name="maxSponsors"/> a day, a 1-30 day window.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync(byte maxSponsors = 1, short maxHeadcount = 200)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @institutionId, @name, '11:30', '13:00', @mode, @maxSponsors, 1, @maxHeadcount, 1, 30, 1,
                    'DonorBrings,InstitutionPrepares', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId,
                institutionId = database.AlphaInstitutionId,
                name = "Changes " + Guid.NewGuid().ToString("N")[..8],
                mode = maxSponsors == 1 ? "Exclusive" : "Shared",
                maxSponsors,
                maxHeadcount,
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

    private HttpClient Alpha() => ClientFor(_alphaOwner, database.AlphaInstitutionId);

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
        JsonElement.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await BodyOf(response)).GetProperty("code").GetString());
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Sponsorship(Guid PublicId, int Id);

    /// <summary>Sets one up through the API: <paramref name="pattern"/> from <paramref name="start"/>, with the pattern's fields.</summary>
    private async Task<Sponsorship> SponsorAsync(Guid donor, Guid slot, string pattern, DateOnly start, DateOnly? end = null, short headcount = 25, object? weekDays = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["donor"] = donor,
            ["mealSlot"] = slot,
            ["pattern"] = pattern,
            ["startDate"] = Iso(start),
            ["endDate"] = end is { } e ? Iso(e) : null,
            ["headcount"] = headcount,
            ["fulfilment"] = FulfilmentMode.InstitutionPrepares,
            ["weekDays"] = weekDays,
        };

        var response = await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var publicId = (await BodyOf(response)).GetProperty("publicId").GetGuid();
        var id = await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @publicId;", new { publicId });
        return new Sponsorship(publicId, id);
    }

    private sealed class BookingRow
    {
        public long Id { get; init; }
        public string BookingNo { get; init; } = null!;
        public string Source { get; init; } = null!;
        public string Status { get; init; } = null!;
        public Guid? DonorUserId { get; init; }
        public int? StandingSponsorshipId { get; init; }
        public short Headcount { get; init; }
        public string? CancelReason { get; init; }
        public DateTime SlotDate { get; init; }

        public DateOnly Date => DateOnly.FromDateTime(SlotDate);
    }

    private const string BookingColumns =
        """
        SELECT b.Id, b.BookingNo, b.Source, b.Status, b.DonorUserId, b.StandingSponsorshipId, b.Headcount, b.CancelReason, si.SlotDate
        FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId
        """;

    private async Task<List<BookingRow>> BookingsOfAsync(int sponsorshipId)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            BookingColumns + "\nWHERE b.StandingSponsorshipId = @sponsorshipId ORDER BY si.SlotDate, b.Id;",
            new { sponsorshipId })];
    }

    private async Task<List<BookingRow>> BookingsOnAsync(int slotId, DateOnly date)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            BookingColumns + "\nWHERE si.MealSlotId = @slotId AND si.SlotDate = @date ORDER BY b.Id;",
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) })];
    }

    private static BookingRow Active(IEnumerable<BookingRow> bookings, DateOnly date) =>
        Assert.Single(bookings, b => b.Date == date && b.Status == BookingStatus.Approved);

    private Task<int> BookedSponsorsAsync(int slotId, DateOnly date) =>
        ScalarAsync<int>(
            "SELECT ISNULL(MAX(BookedSponsors), 0) FROM booking.SlotInstances WHERE MealSlotId = @slotId AND SlotDate = @date;",
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) });

    private Task<int> BookedHeadcountAsync(int slotId, DateOnly date) =>
        ScalarAsync<int>(
            "SELECT ISNULL(MAX(BookedHeadcount), 0) FROM booking.SlotInstances WHERE MealSlotId = @slotId AND SlotDate = @date;",
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) });

    private sealed record DayChangeRow(string Kind, DateTime? OtherDate, long? BookingId, int? DayRequestId, string? Reason);

    private async Task<List<(DateOnly Date, DayChangeRow Change)>> DayChangesAsync(int sponsorshipId)
    {
        await using var connection = await PlatformConnectionAsync();
        var rows = await connection.QueryAsync<(DateTime Date, string Kind, DateTime? OtherDate, long? BookingId, int? DayRequestId, string? Reason)>(
            """
            SELECT ExceptionDate, Kind, OtherDate, BookingId, DayRequestId, Reason
            FROM booking.StandingExceptions WHERE StandingSponsorshipId = @sponsorshipId ORDER BY ExceptionDate;
            """,
            new { sponsorshipId });

        return [.. rows.Select(r => (DateOnly.FromDateTime(r.Date), new DayChangeRow(r.Kind, r.OtherDate, r.BookingId, r.DayRequestId, r.Reason)))];
    }

    private static DateTime At(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    /// <summary>The standing job as it runs at 00:45 on <paramref name="today"/>, as the platform.</summary>
    private async Task<StandingRun> RunAtAsync(DateOnly today)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        var clock = new FixedTimeProvider(SriLankaTime.ToUtc(today, new TimeOnly(0, 45)));
        var booker = new StandingDayBooker(context, new NoLocks(), new SqlStandingLocks(context), new SqlSlotInventory(context), new SqlNumberSeries(context), clock);
        return await new StandingBookingRun(context, booker, clock).RunAsync();
    }

    private SqlConnectionFactory PlatformConnections() => new(database.ConnectionString, TestCurrentUserContext.Platform());

    /// <summary>Sends every text waiting in the outbox through the real handler, templates and the console sender.</summary>
    private async Task SendTextsAsync()
    {
        var connections = PlatformConnections();
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);
        var processor = new OutboxProcessor(
            connections,
            [new StandingSmsHandler(new BookingNotificationQueries(connections), texts)],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

        await processor.RunAsync();
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType IN ('StandingTextDue', 'DayRequestAnswered') AND ProcessedAt IS NULL;",
            new { }));
    }

    private sealed record Text(string Body, string DedupKey);

    private async Task<List<Text>> TextsToAsync(string phone)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<Text>(
            "SELECT Body AS Body, DedupKey AS DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;",
            new { phone })];
    }

    private Task<string> PhoneOfAsync(Guid userId) =>
        ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @userId;", new { userId });

    // ------------------------------------------------------------------ asking for a held day (K6)

    private async Task<(int SlotId, Guid Slot, Sponsorship Standing, string SponsorPhone, DateOnly Day)> HeldDayAsync(byte maxSponsors = 1, short maxHeadcount = 200)
    {
        var (slotId, slot) = await SlotAsync(maxSponsors, maxHeadcount);
        var (donor, phone) = await DonorAsync();
        var day = Today.AddDays(3);
        var standing = await SponsorAsync(donor, slot, StandingPattern.Weekly, day, day.AddDays(21), weekDays: new[] { (int)day.DayOfWeek });
        return (slotId, slot, standing, phone, day);
    }

    private static Dictionary<string, object?> Ask(Guid slot, DateOnly day, short headcount = 30) =>
        new()
        {
            ["mealSlot"] = slot,
            ["date"] = Iso(day),
            ["headcount"] = headcount,
            ["fulfilment"] = FulfilmentMode.DonorBrings,
            ["note"] = "  For my father's anniversary  ",
        };

    private async Task<string> AskAsync(Guid asker, Guid slot, DateOnly day, short headcount = 30)
    {
        var response = await SendAsync(ClientFor(asker, null), HttpMethod.Post, $"/api/institutions/{_alphaSlug}/day-requests", Ask(slot, day, headcount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var requestNo = (await BodyOf(response)).GetProperty("requestNo").GetString()!;
        Assert.StartsWith("DR-", requestNo, StringComparison.Ordinal);
        return requestNo;
    }

    private async Task<string?> ReasonOnCalendarAsync(Guid slot, DateOnly day)
    {
        using var client = _host.CreateClient();
        var month = await client.GetFromJsonAsync<JsonElement>($"/api/institutions/{_alphaSlug}/availability?month={day:yyyy-MM}");
        var days = month.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("mealSlot").GetGuid() == slot).GetProperty("days");
        var found = days.EnumerateArray().Single(d => d.GetProperty("date").GetString() == Iso(day));
        return found.GetProperty("reason").ValueKind == JsonValueKind.Null ? null : found.GetProperty("reason").GetString();
    }

    [Fact]
    public async Task AStandingDay_ReadsAsHeld_OnThePublicCalendar_AndAnOrdinaryFullDayDoesNot()
    {
        var (_, slot, _, _, day) = await HeldDayAsync();
        Assert.Equal(BookingErrors.Held, await ReasonOnCalendarAsync(slot, day));

        // The same slot, a day another donor booked: full, and nobody to ask.
        var other = day.AddDays(1);
        var response = await SendAsync(ClientFor(await AskerAsync(), null), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(other), headcount = 10, fulfilment = FulfilmentMode.DonorBrings });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(BookingErrors.SlotFull, await ReasonOnCalendarAsync(slot, other));

        // A slot with room left on a standing day is simply bookable.
        var (_, roomy, _, _, roomyDay) = await HeldDayAsync(maxSponsors: 3);
        Assert.Null(await ReasonOnCalendarAsync(roomy, roomyDay));
    }

    [Fact]
    public async Task ADonorAsksForAHeldDay_OnceWhileOpen_AndAskingHoldsNothing()
    {
        var (slotId, slot, standing, _, day) = await HeldDayAsync();
        var asker = await AskerAsync();

        var requestNo = await AskAsync(asker, slot, day);

        var request = await ScalarAsync<string>(
            "SELECT CONCAT(Status, '|', Headcount, '|', FulfilmentMode, '|', Note) FROM booking.DayRequests WHERE RequestNo = @requestNo;",
            new { requestNo });
        Assert.Equal("Open|30|DonorBrings|For my father's anniversary", request);

        // The day is still the sponsor's.
        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(standing.Id), day).Status);
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));

        await AssertRefusedAsync(
            await SendAsync(ClientFor(asker, null), HttpMethod.Post, $"/api/institutions/{_alphaSlug}/day-requests", Ask(slot, day)),
            HttpStatusCode.Conflict,
            DayRequestResult.AlreadyAsked);

        // A day nobody holds is booked, not asked for.
        await AssertRefusedAsync(
            await SendAsync(ClientFor(asker, null), HttpMethod.Post, $"/api/institutions/{_alphaSlug}/day-requests", Ask(slot, day.AddDays(1))),
            HttpStatusCode.Conflict,
            DayRequestResult.NotHeld);

        // The asker sees it as theirs; the institution sees it in its tray with who holds the day.
        var mine = await BodyOf(await SendAsync(ClientFor(asker, null), HttpMethod.Get, "/api/bookings/day-requests"));
        Assert.Equal(requestNo, Assert.Single(mine.EnumerateArray()).GetProperty("requestNo").GetString());

        var tray = await BodyOf(await SendAsync(Alpha(), HttpMethod.Get, Tray));
        var row = Assert.Single(tray.EnumerateArray(), r => r.GetProperty("requestNo").GetString() == requestNo);
        Assert.Equal(standing.PublicId, Assert.Single(row.GetProperty("holders").EnumerateArray()).GetProperty("sponsorship").GetGuid());

        // Another institution sees none of it, and cannot give it.
        var beta = await BodyOf(await SendAsync(Beta(), HttpMethod.Get, Tray));
        Assert.DoesNotContain(beta.EnumerateArray(), r => r.GetProperty("requestNo").GetString() == requestNo);
        await AssertRefusedAsync(await SendAsync(Beta(), HttpMethod.Post, $"{Tray}/{requestNo}/give", new { }), HttpStatusCode.NotFound, DayRequestResult.NotFound);
    }

    [Fact]
    public async Task GivingADay_LetsTheStandingDayGo_AndBooksTheAskerApproved_TextingBothOnce()
    {
        var (slotId, slot, standing, sponsorPhone, day) = await HeldDayAsync();
        var asker = await AskerAsync();
        var requestNo = await AskAsync(asker, slot, day);

        var response = await SendAsync(Alpha(), HttpMethod.Post, $"{Tray}/{requestNo}/give", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bookingNo = (await BodyOf(response)).GetProperty("bookingNo").GetString()!;

        var onDay = await BookingsOnAsync(slotId, day);
        var released = Assert.Single(onDay, b => b.StandingSponsorshipId == standing.Id);
        Assert.Equal((BookingStatus.Cancelled, DayRequestHandler.GivenReason), (released.Status, released.CancelReason));
        var given = Assert.Single(onDay, b => b.BookingNo == bookingNo);
        Assert.Equal((Booking.OnlineSource, BookingStatus.Approved, (Guid?)asker, (short)30), (given.Source, given.Status, given.DonorUserId, given.Headcount));
        Assert.Equal((1, 30), (await BookedSponsorsAsync(slotId, day), await BookedHeadcountAsync(slotId, day)));

        var requestId = await ScalarAsync<int>("SELECT Id FROM booking.DayRequests WHERE RequestNo = @requestNo;", new { requestNo });
        var change = Assert.Single(await DayChangesAsync(standing.Id));
        Assert.Equal((day, new DayChangeRow(StandingDayChange.Released, null, released.Id, requestId, null)), change);
        Assert.Equal($"Given|{standing.Id}|{given.Id}", await ScalarAsync<string>(
            "SELECT CONCAT(Status, '|', StandingSponsorshipId, '|', BookingId) FROM booking.DayRequests WHERE Id = @requestId;", new { requestId }));

        // The asker holds the day now: full, and no longer held by a regular donor.
        Assert.Equal(BookingErrors.SlotFull, await ReasonOnCalendarAsync(slot, day));

        // The sponsor's other days stay theirs, and the job never books this one back.
        Assert.All((await BookingsOfAsync(standing.Id)).Where(b => b.Date != day), b => Assert.Equal(BookingStatus.Approved, b.Status));
        await RunAtAsync(Today);
        Assert.Single(await BookingsOfAsync(standing.Id), b => b.Date == day);

        // Answered: asking again for it is a new request, and giving this one again is refused.
        await AssertRefusedAsync(await SendAsync(Alpha(), HttpMethod.Post, $"{Tray}/{requestNo}/give", new { }), HttpStatusCode.Conflict, DayRequestResult.Answered);

        await SendTextsAsync();
        await SendTextsAsync();
        var sponsorTexts = await TextsToAsync(sponsorPhone);
        var releasedText = Assert.Single(sponsorTexts, t => t.DedupKey.StartsWith(StandingTexts.Released, StringComparison.Ordinal));
        Assert.Contains(Iso(day), releasedText.Body, StringComparison.Ordinal);
        var askerText = Assert.Single(await TextsToAsync(await PhoneOfAsync(asker)));
        Assert.Equal($"{StandingTexts.RequestGiven}:{requestNo}", askerText.DedupKey);
        Assert.StartsWith($"Sethika {bookingNo}: ", askerText.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGiveThatCannotBookTheAsker_LeavesTheStandingDayAsItWas()
    {
        // Room for 40 people: the sponsor brings 25, the asker 30. Releasing the sponsor's 25 and
        // taking the asker's 30 fits; another booking of 15 on the day makes it not fit.
        var (slotId, slot, standing, _, day) = await HeldDayAsync(maxSponsors: 2, maxHeadcount: 40);
        var asker = await AskerAsync();

        // A second sponsor place is free, so the day is not held yet: fill it first.
        var response = await SendAsync(ClientFor(await AskerAsync(), null), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(day), headcount = 15, fulfilment = FulfilmentMode.DonorBrings });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var requestNo = await AskAsync(asker, slot, day, headcount: 30);

        await AssertRefusedAsync(await SendAsync(Alpha(), HttpMethod.Post, $"{Tray}/{requestNo}/give", new { }), HttpStatusCode.Conflict, BookingErrors.SlotFull);

        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(standing.Id), day).Status);
        Assert.Empty(await DayChangesAsync(standing.Id));
        Assert.Equal((2, 40), (await BookedSponsorsAsync(slotId, day), await BookedHeadcountAsync(slotId, day)));
        Assert.Equal("Open", await ScalarAsync<string>("SELECT Status FROM booking.DayRequests WHERE RequestNo = @requestNo;", new { requestNo }));

        // Nobody is told of a give that did not happen: the set-up's own text is all there is.
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE ProcessedAt IS NULL AND (EventType = 'DayRequestAnswered' OR Payload LIKE '%STANDING_RELEASED%');",
            new { }));
    }

    [Fact]
    public async Task Declining_KeepsTheDayTheSponsors_AndTextsTheAsker()
    {
        var (slotId, slot, standing, sponsorPhone, day) = await HeldDayAsync();
        var asker = await AskerAsync();
        var requestNo = await AskAsync(asker, slot, day);

        var response = await SendAsync(Alpha(), HttpMethod.Post, $"{Tray}/{requestNo}/decline", new { reason = " The family has given this day for years " });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal("Declined|The family has given this day for years", await ScalarAsync<string>(
            "SELECT CONCAT(Status, '|', DeclineReason) FROM booking.DayRequests WHERE RequestNo = @requestNo;", new { requestNo }));
        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(standing.Id), day).Status);
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));
        await AssertRefusedAsync(await SendAsync(Alpha(), HttpMethod.Post, $"{Tray}/{requestNo}/give", new { }), HttpStatusCode.Conflict, DayRequestResult.Answered);

        await SendTextsAsync();
        var text = Assert.Single(await TextsToAsync(await PhoneOfAsync(asker)));
        Assert.Equal($"{StandingTexts.RequestDeclined}:{requestNo}", text.DedupKey);
        Assert.DoesNotContain("years", text.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(await TextsToAsync(sponsorPhone), t => t.DedupKey.StartsWith(StandingTexts.Released, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ blocks over standing days (K7)

    [Fact]
    public async Task ABlock_LetsTheStandingDaysGo_TextsTheSponsorOnce_AndListsOrdinaryBookings()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 2);
        var (donor, phone) = await DonorAsync();
        var first = Today.AddDays(3);
        var standing = await SponsorAsync(donor, slot, StandingPattern.Daily, first, first.AddDays(40));

        // As if it began a week ago: its days before today are past, and a block does not touch them.
        await ExecuteAsync("UPDATE booking.StandingSponsorships SET StartDate = @start WHERE Id = @id;", new { start = At(Today.AddDays(-7)), id = standing.Id });

        // An ordinary booking on a blocked day stays, and is named.
        var response = await SendAsync(ClientFor(await AskerAsync(), null), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(first.AddDays(1)), headcount = 10, fulfilment = FulfilmentMode.DonorBrings });
        var ordinary = (await BodyOf(response)).GetProperty("bookingNo").GetString();

        // From two days ago to a day beyond the window (which ends 30 days from today): today on is let go.
        var from = Today;
        var to = Today.AddDays(31);
        response = await SendAsync(Alpha(), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(Today.AddDays(-2)), dateTo = Iso(to), mealSlot = slot, reason = "Kathina ceremony" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var block = await BodyOf(response);
        Assert.Equal(to.DayNumber - from.DayNumber + 1, block.GetProperty("standingDays").GetInt32());
        Assert.Equal(ordinary, Assert.Single(block.GetProperty("bookings").EnumerateArray()).GetString());

        var bookings = await BookingsOfAsync(standing.Id);
        Assert.All(bookings.Where(b => b.Date >= from && b.Date <= to), b => Assert.Equal((BookingStatus.Cancelled, "Kathina ceremony"), (b.Status, b.CancelReason)));
        Assert.Equal(0, await BookedSponsorsAsync(slotId, first));
        Assert.Equal(1, await BookedSponsorsAsync(slotId, first.AddDays(1)));

        var changes = await DayChangesAsync(standing.Id);
        Assert.Equal(Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)), changes.Select(c => c.Date));
        Assert.All(changes, c => Assert.Equal((StandingDayChange.Blocked, "Kathina ceremony"), (c.Change.Kind, c.Change.Reason)));
        Assert.Null(changes[0].Change.BookingId);
        Assert.Null(changes[^1].Change.BookingId);
        Assert.Equal(bookings.Single(b => b.Date == first).Id, changes.Single(c => c.Date == first).Change.BookingId);

        // Lifting the block does not bring the days back: the sponsor was told they are gone.
        var id = block.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Delete, $"/api/institution/blocked-dates/{id}")).StatusCode);
        await RunAtAsync(Today.AddDays(2));
        Assert.DoesNotContain(await BookingsOfAsync(standing.Id), b => b.Date >= from && b.Date <= to && b.Status == BookingStatus.Approved);

        await SendTextsAsync();
        var text = Assert.Single(await TextsToAsync(phone), t => t.DedupKey.StartsWith(StandingTexts.Blocked, StringComparison.Ordinal));
        Assert.Contains($"{Iso(from)} සිට {Iso(to)} දක්වා", text.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockOnOneSlot_LeavesAnotherSlotsStandingDays()
    {
        var (_, blocked) = await SlotAsync();
        var (_, other) = await SlotAsync();
        var day = Today.AddDays(4);
        var onBlocked = await SponsorAsync((await DonorAsync()).PublicId, blocked, StandingPattern.Once, day);
        var onOther = await SponsorAsync((await DonorAsync()).PublicId, other, StandingPattern.Once, day);

        var response = await SendAsync(Alpha(), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(day), dateTo = Iso(day), mealSlot = blocked, reason = "Pirith" });
        Assert.Equal(1, (await BodyOf(response)).GetProperty("standingDays").GetInt32());

        Assert.Equal(BookingStatus.Cancelled, Assert.Single(await BookingsOfAsync(onBlocked.Id)).Status);
        Assert.Equal(BookingStatus.Approved, Assert.Single(await BookingsOfAsync(onOther.Id)).Status);
        Assert.Empty(await DayChangesAsync(onOther.Id));
    }

    [Fact]
    public async Task TheTexts_ReachALinkedAccount_AndADonorWithoutOne_OnlyWithConsent()
    {
        var (_, slot) = await SlotAsync(maxSponsors: 3);
        var day = Today.AddDays(5);
        var account = await AskerAsync(language: "ta");
        var accountPhone = await PhoneOfAsync(account);
        var (linked, bookPhone) = await DonorAsync(consent: false, userId: account);
        var (unconsenting, silentPhone) = await DonorAsync(consent: false);
        var (consenting, textedPhone) = await DonorAsync(consent: true, language: "en");

        foreach (var donor in new[] { linked, unconsenting, consenting })
        {
            await SponsorAsync(donor, slot, StandingPattern.Once, day);
        }

        await SendTextsAsync();

        // A linked donor is texted as their account, in its language, at its confirmed phone.
        var toAccount = Assert.Single(await TextsToAsync(accountPhone));
        Assert.StartsWith($"{StandingTexts.Created}:", toAccount.DedupKey, StringComparison.Ordinal);
        Assert.Contains("நீங்கள்", toAccount.Body, StringComparison.Ordinal);
        if (bookPhone != accountPhone)
        {
            Assert.Empty(await TextsToAsync(bookPhone));
        }

        Assert.Empty(await TextsToAsync(silentPhone));
        var toConsenting = Assert.Single(await TextsToAsync(textedPhone));
        Assert.Contains($"{Iso(day)} 11:30. You are now a regular donor here", toConsenting.Body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ swaps (K8)

    private sealed record Pair(int SlotId, Guid Slot, Sponsorship A, string PhoneA, DateOnly DayA, Sponsorship B, string PhoneB, DateOnly DayB);

    /// <summary>A gives every week on <c>DayA</c>'s weekday, B on <c>DayB</c>'s, one sponsor a day.</summary>
    private async Task<Pair> PairAsync(short headcountA = 25, short headcountB = 25, short maxHeadcount = 200)
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1, maxHeadcount: maxHeadcount);
        var (donorA, phoneA) = await DonorAsync();
        var (donorB, phoneB) = await DonorAsync();
        var dayA = Today.AddDays(3);
        var dayB = Today.AddDays(5);
        var a = await SponsorAsync(donorA, slot, StandingPattern.Weekly, dayA, headcount: headcountA, weekDays: new[] { (int)dayA.DayOfWeek });
        var b = await SponsorAsync(donorB, slot, StandingPattern.Weekly, dayB, headcount: headcountB, weekDays: new[] { (int)dayB.DayOfWeek });
        return new Pair(slotId, slot, a, phoneA, dayA, b, phoneB, dayB);
    }

    private static object Swap(Sponsorship a, DateOnly dayA, Sponsorship b, DateOnly dayB, bool permanent = false) =>
        new { a = new { sponsorship = a.PublicId, date = Iso(dayA) }, b = new { sponsorship = b.PublicId, date = Iso(dayB) }, permanent };

    [Fact]
    public async Task SwappingThisTime_MovesBothDays_InOneTransaction()
    {
        var p = await PairAsync(headcountA: 25, headcountB: 40);

        var response = await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var a = await BookingsOfAsync(p.A.Id);
        var b = await BookingsOfAsync(p.B.Id);
        Assert.Equal(StandingSwapHandler.SwappedReason, Assert.Single(a, x => x.Date == p.DayA).CancelReason);
        Assert.Equal(StandingSwapHandler.SwappedReason, Assert.Single(b, x => x.Date == p.DayB).CancelReason);
        Assert.Equal((short)25, Active(a, p.DayB).Headcount);
        Assert.Equal((short)40, Active(b, p.DayA).Headcount);
        Assert.Equal((1, 40), (await BookedSponsorsAsync(p.SlotId, p.DayA), await BookedHeadcountAsync(p.SlotId, p.DayA)));
        Assert.Equal((1, 25), (await BookedSponsorsAsync(p.SlotId, p.DayB), await BookedHeadcountAsync(p.SlotId, p.DayB)));

        Assert.Equal(
            [(p.DayA, StandingDayChange.MovedTo, (DateTime?)At(p.DayB)), (p.DayB, StandingDayChange.MovedFrom, At(p.DayA))],
            (await DayChangesAsync(p.A.Id)).Select(c => (c.Date, c.Change.Kind, c.Change.OtherDate)));
        Assert.Equal(
            [(p.DayA, StandingDayChange.MovedFrom, (DateTime?)At(p.DayB)), (p.DayB, StandingDayChange.MovedTo, At(p.DayA))],
            (await DayChangesAsync(p.B.Id)).Select(c => (c.Date, c.Change.Kind, c.Change.OtherDate)));

        // A swap's MovedFrom names the booking it made, its MovedTo the one it cancelled.
        Assert.Equal(Active(a, p.DayB).Id, (await DayChangesAsync(p.A.Id)).Single(c => c.Date == p.DayB).Change.BookingId);
        Assert.Equal(a.Single(x => x.Date == p.DayA).Id, (await DayChangesAsync(p.A.Id)).Single(c => c.Date == p.DayA).Change.BookingId);

        // The job books neither back; the next weeks are untouched.
        await RunAtAsync(Today);
        Assert.Single(await BookingsOfAsync(p.A.Id), x => x.Date == p.DayA);
        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(p.A.Id), p.DayA.AddDays(7)).Status);

        // Swapping the same day again is refused.
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA.AddDays(7), p.B, p.DayB)),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.DayAlreadyChanged);

        await SendTextsAsync();
        var toA = Assert.Single(await TextsToAsync(p.PhoneA), t => t.DedupKey.StartsWith(StandingTexts.Swapped + ":", StringComparison.Ordinal));
        Assert.Contains($"{Iso(p.DayB)} 11:30. ඔබේ {Iso(p.DayA)} දිනය", toA.Body, StringComparison.Ordinal);
        Assert.Single(await TextsToAsync(p.PhoneB), t => t.DedupKey.StartsWith(StandingTexts.Swapped + ":", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASwapThatCannotBook_LeavesBothDaysAsTheyWere()
    {
        // B brings 30; A's day, once A's 25 are let go, has room for 29: B cannot be booked on it.
        var p = await PairAsync(headcountA: 25, headcountB: 30, maxHeadcount: 30);
        await ExecuteAsync("UPDATE booking.SlotInstances SET MaxHeadcount = 29 WHERE MealSlotId = @slotId AND SlotDate = @day;", new { slotId = p.SlotId, day = At(p.DayA) });

        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB)),
            HttpStatusCode.Conflict,
            BookingErrors.SlotFull);

        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(p.A.Id), p.DayA).Status);
        Assert.Equal(BookingStatus.Approved, Active(await BookingsOfAsync(p.B.Id), p.DayB).Status);
        Assert.Empty(await DayChangesAsync(p.A.Id));
        Assert.Empty(await DayChangesAsync(p.B.Id));
        Assert.Equal((1, 25), (await BookedSponsorsAsync(p.SlotId, p.DayA), await BookedHeadcountAsync(p.SlotId, p.DayA)));
        Assert.Equal((1, 30), (await BookedSponsorsAsync(p.SlotId, p.DayB), await BookedHeadcountAsync(p.SlotId, p.DayB)));
    }

    [Fact]
    public async Task ASwapIsRefused_ForDaysItCannotMove()
    {
        var p = await PairAsync();
        var (_, otherSlot) = await SlotAsync();
        var elsewhere = await SponsorAsync((await DonorAsync()).PublicId, otherSlot, StandingPattern.Once, p.DayB);

        // Another meal, a day not booked yet, a one-day sponsorship for good.
        await AssertRefusedAsync(await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, elsewhere, p.DayB)), HttpStatusCode.BadRequest, "invalid_details");
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA.AddDays(35), p.B, p.DayB)),
            HttpStatusCode.Conflict,
            StandingSwapHandler.DayNotBooked);
        var once = await SponsorAsync((await DonorAsync()).PublicId, p.Slot, StandingPattern.Once, p.DayA.AddDays(3));
        await AssertRefusedAsync(await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, once, p.DayA.AddDays(3), permanent: true)), HttpStatusCode.BadRequest, "invalid_details");

        // For good, but A has a day let go after its day: that change would have no owner.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/{p.A.PublicId}/skip", new { date = Iso(p.DayA.AddDays(14)) })).StatusCode);
        await AssertRefusedAsync(
            await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB, permanent: true)),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.DayAlreadyChanged);
        Assert.Equal("Active", await ScalarAsync<string>("SELECT Status FROM booking.StandingSponsorships WHERE Id = @id;", new { id = p.A.Id }));

        // Another institution's swap of these is not found.
        await AssertRefusedAsync(await SendAsync(Beta(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB)), HttpStatusCode.NotFound, SponsorshipChangeResult.NotFound);
        Assert.Single(await DayChangesAsync(p.A.Id));
    }

    [Fact]
    public async Task SwappingFromNowOn_EndsBoth_AndEachGivesOnTheOthersDaysFromItsDay()
    {
        var p = await PairAsync(headcountA: 25, headcountB: 40);

        var response = await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB, permanent: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);
        var newA = await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @id;", new { id = body.GetProperty("a").GetGuid() });
        var newB = await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @id;", new { id = body.GetProperty("b").GetGuid() });

        // The old ones ended. Each began on its own swapped day, so it has no day before it to end on.
        Assert.Equal("Ended|", await ScalarAsync<string>("SELECT CONCAT(Status, '|', EndDate) FROM booking.StandingSponsorships WHERE Id = @id;", new { id = p.A.Id }));
        Assert.Equal("Ended|", await ScalarAsync<string>("SELECT CONCAT(Status, '|', EndDate) FROM booking.StandingSponsorships WHERE Id = @id;", new { id = p.B.Id }));
        Assert.DoesNotContain(await BookingsOfAsync(p.A.Id), x => x.Status == BookingStatus.Approved && x.Date >= p.DayA);
        Assert.DoesNotContain(await BookingsOfAsync(p.B.Id), x => x.Status == BookingStatus.Approved && x.Date >= p.DayB);

        // A now gives 25 on B's weekday from B's day, B gives 40 on A's weekday from A's day.
        var aDays = (await BookingsOfAsync(newA)).Where(x => x.Status == BookingStatus.Approved).ToList();
        var bDays = (await BookingsOfAsync(newB)).Where(x => x.Status == BookingStatus.Approved).ToList();
        Assert.Equal(p.DayB, aDays[0].Date);
        Assert.All(aDays, x => Assert.Equal((p.DayB.DayOfWeek, (short)25), (x.Date.DayOfWeek, x.Headcount)));
        Assert.Equal(p.DayA, bDays[0].Date);
        Assert.All(bDays, x => Assert.Equal((p.DayA.DayOfWeek, (short)40), (x.Date.DayOfWeek, x.Headcount)));
        Assert.Equal((1, 40), (await BookedSponsorsAsync(p.SlotId, p.DayA), await BookedHeadcountAsync(p.SlotId, p.DayA)));

        // The job carries on with the new ones and books nothing for the old.
        var before = aDays.Count + bDays.Count;
        await RunAtAsync(Today);
        Assert.Equal(before, (await BookingsOfAsync(newA)).Count + (await BookingsOfAsync(newB)).Count);

        await SendTextsAsync();
        Assert.Single(await TextsToAsync(p.PhoneA), t => t.DedupKey.StartsWith(StandingTexts.SwappedOn + ":", StringComparison.Ordinal));
        Assert.Single(await TextsToAsync(p.PhoneB), t => t.DedupKey.StartsWith(StandingTexts.SwappedOn + ":", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABlockOverASwappedInDay_LetsTheSwappedInSponsorGo()
    {
        var p = await PairAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/swap", Swap(p.A, p.DayA, p.B, p.DayB))).StatusCode);
        var movedIn = Active(await BookingsOfAsync(p.A.Id), p.DayB);

        var response = await SendAsync(Alpha(), HttpMethod.Post, "/api/institution/blocked-dates",
            new { dateFrom = Iso(p.DayB), dateTo = Iso(p.DayB), mealSlot = p.Slot, reason = "Pirith" });
        Assert.Equal(1, (await BodyOf(response)).GetProperty("standingDays").GetInt32());

        Assert.Equal(BookingStatus.Cancelled, (await BookingsOfAsync(p.A.Id)).Single(x => x.Id == movedIn.Id).Status);
        Assert.Equal(0, await BookedSponsorsAsync(p.SlotId, p.DayB));
        Assert.Equal(
            new DayChangeRow(StandingDayChange.Blocked, null, movedIn.Id, null, "Pirith"),
            (await DayChangesAsync(p.A.Id)).Single(c => c.Date == p.DayB).Change);

        // B gave the day away already: its change stays what it was.
        Assert.Equal(StandingDayChange.MovedTo, (await DayChangesAsync(p.B.Id)).Single(c => c.Date == p.DayB).Change.Kind);
    }

    [Fact]
    public async Task ASkip_TextsTheSponsor_Once()
    {
        var (_, slot) = await SlotAsync();
        var (donor, phone) = await DonorAsync(language: "en");
        var day = Today.AddDays(4);
        var standing = await SponsorAsync(donor, slot, StandingPattern.Once, day);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/{standing.PublicId}/skip", new { date = Iso(day) })).StatusCode);
        await SendTextsAsync();
        await SendTextsAsync();

        var text = Assert.Single(await TextsToAsync(phone), t => t.DedupKey.StartsWith(StandingTexts.Skipped, StringComparison.Ordinal));
        Assert.Contains($"{Iso(day)} 11:30. As asked, you will not give on this day", text.Body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the races, forced (K19)

    /// <summary>
    /// Holds an update lock on <paramref name="sponsorshipId"/>'s row from another connection, starts
    /// <paramref name="changes"/> with no Redis lock, waits until every one of them is queued on the row,
    /// then lets them go: they run one at a time in whatever order SQL Server grants the lock.
    /// </summary>
    private async Task<T[]> RaceAsync<T>(int sponsorshipId, params Func<Task<T>>[] changes)
    {
        await using var holder = await PlatformConnectionAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM booking.StandingSponsorships WITH (UPDLOCK, ROWLOCK) WHERE Id = @sponsorshipId;",
            new { sponsorshipId },
            transaction);

        var running = changes.Select(change => Task.Run(change)).ToArray();

        await using (var observer = await PlatformConnectionAsync())
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await observer.ExecuteScalarAsync<int>(
                       """
                       SELECT COUNT(*) FROM sys.dm_tran_locks l
                       JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                       WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID()
                         AND p.object_id = OBJECT_ID('booking.StandingSponsorships');
                       """) < changes.Length)
            {
                Assert.False(running.Any(t => t.IsCompleted), "A change did not wait for the sponsorship's row lock.");
                Assert.True(DateTime.UtcNow < deadline, "The race was not forced.");
                await Task.Delay(25);
            }
        }

        await transaction.CommitAsync();
        return await Task.WhenAll(running);
    }

    private TestCurrentUserContext AlphaOwnerSession() => new() { UserId = _alphaOwner, InstitutionId = database.AlphaInstitutionId };

    private async Task<SwapResult> SwapWithoutRedisAsync(SwapInput input)
    {
        await using var context = ContextFor(AlphaOwnerSession());
        var handler = new StandingSwapHandler(context, new NoLocks(), new SqlStandingLocks(context), new SqlSlotInventory(context), new SqlNumberSeries(context), TimeProvider.System);
        return await handler.SwapAsync(database.AlphaInstitutionId, _alphaOwner, input);
    }

    [Fact]
    public async Task TwoStaffSwappingTheSameDayAtOnce_OneWins()
    {
        var p = await PairAsync();
        var (donorC, _) = await DonorAsync();
        var dayC = Today.AddDays(6);
        var c = await SponsorAsync(donorC, p.Slot, StandingPattern.Once, dayC);

        // A has the lowest id, so both swaps queue on A's row.
        Assert.True(p.A.Id < p.B.Id && p.A.Id < c.Id);
        var withB = new SwapInput(new SwapDay(p.A.PublicId, p.DayA), new SwapDay(p.B.PublicId, p.DayB), false);
        var withC = new SwapInput(new SwapDay(p.A.PublicId, p.DayA), new SwapDay(c.PublicId, dayC), false);

        var results = await RaceAsync(p.A.Id, () => SwapWithoutRedisAsync(withB), () => SwapWithoutRedisAsync(withC));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == SponsorshipChangeResult.DayAlreadyChanged);

        // A's day went to exactly one of them, and every day has one sponsor.
        var onDayA = (await BookingsOnAsync(p.SlotId, p.DayA)).Where(x => x.Status == BookingStatus.Approved).ToList();
        Assert.Single(onDayA);
        Assert.NotEqual(p.A.Id, onDayA[0].StandingSponsorshipId);
        foreach (var day in new[] { p.DayA, p.DayB, dayC })
        {
            Assert.Equal(1, await BookedSponsorsAsync(p.SlotId, day));
        }

        Assert.Equal(2, (await DayChangesAsync(p.A.Id)).Count);
    }

    private async Task<DayRequestResult> GiveWithoutRedisAsync(string requestNo)
    {
        await using var context = ContextFor(AlphaOwnerSession());
        var handler = new DayRequestHandler(
            context, new NoLocks(), new SqlStandingLocks(context), new SqlSlotInventory(context), new SqlNumberSeries(context),
            new SqlStandingHolds(database.ConnectionString), TimeProvider.System);
        return await handler.GiveAsync(database.AlphaInstitutionId, _alphaOwner, requestNo, null);
    }

    [Fact]
    public async Task ABlockAndTheJobAtOnce_LeaveTheDayLetGo_WhicheverGoesFirst()
    {
        // A day beyond today's window, which the job books on a later night as the block is added.
        var (slotId, slot) = await SlotAsync();
        var day = Today.AddDays(35);
        var standing = await SponsorAsync((await DonorAsync()).PublicId, slot, StandingPattern.Once, day);
        Assert.Empty(await BookingsOfAsync(standing.Id));

        var results = await RaceAsync<object>(
            standing.Id,
            async () =>
            {
                await using var context = ContextFor(AlphaOwnerSession());
                var handler = new SlotManagementHandler(context, new StandingBlocks(context, new SqlStandingLocks(context), new SqlSlotInventory(context)), TimeProvider.System);
                return await handler.AddBlockedDateAsync(database.AlphaInstitutionId, _alphaOwner, new BlockedDateInput(day, day, slot, "Kathina"));
            },
            async () =>
            {
                await using var context = ContextFor(TestCurrentUserContext.Platform());
                var clock = new FixedTimeProvider(SriLankaTime.ToUtc(day.AddDays(-5), new TimeOnly(0, 45)));
                var booker = new StandingDayBooker(context, new NoLocks(), new SqlStandingLocks(context), new SqlSlotInventory(context), new SqlNumberSeries(context), clock);
                return await booker.BookDayAsync(standing.Id, day);
            });

        Assert.True(((SlotChangeResult)results[0]).Succeeded);
        Assert.DoesNotContain(await BookingsOfAsync(standing.Id), b => b.Status == BookingStatus.Approved);
        Assert.Equal(0, await BookedSponsorsAsync(slotId, day));
        Assert.Equal(StandingDayChange.Blocked, Assert.Single(await DayChangesAsync(standing.Id)).Change.Kind);
    }

    [Fact]
    public async Task TwoAskersGivenTheSameDayAtOnce_OnlyOneGetsIt()
    {
        var (slotId, slot, standing, _, day) = await HeldDayAsync();
        var first = await AskAsync(await AskerAsync(), slot, day);
        var second = await AskAsync(await AskerAsync(), slot, day);

        var results = await RaceAsync(standing.Id, () => GiveWithoutRedisAsync(first), () => GiveWithoutRedisAsync(second));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == DayRequestResult.NotHeld);
        Assert.Single(await BookingsOnAsync(slotId, day), x => x.Status == BookingStatus.Approved);
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));
        Assert.Single(await DayChangesAsync(standing.Id));
    }
}

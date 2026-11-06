using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Discovery;
using Sethika.Application.Giving.Reports;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Giving;

/// <summary>
/// Plan 09, D.4 against real SQL Server: the monthly report (M16), its files (M17, M27), and the Day
/// Ledger's giving line (M18).
/// </summary>
/// <remarks>
/// Utilisation depends on the day, so those tests call the handler with a fixed clock on March 2030, a
/// month worked out by hand: it starts on a Friday and has 21 weekdays. They run as the platform, which
/// sees every row, so they prove the queries' own institution filter. The HTTP tests prove the
/// permissions, the wire format and RLS.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ReportEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "report-test-signing-key-at-least-32-bytes!";
    private const string Report = "/api/institution/reports/monthly";
    private const string Donations = "/api/institution/donations";
    private const string Needs = "/api/institution/needs";
    private const string Summary = "/api/institution/giving/summary";
    private const byte Weekdays = 0b0111110;

    private static readonly DateOnly March = new(2030, 3, 1);

    /// <summary>A day in April 2030: March is over.</summary>
    private static readonly DateTime AfterMarch = new(2030, 4, 10, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>Long before March 2030, so a slot made then served all of it.</summary>
    private static readonly DateTime LongAgo = new(2029, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    private Guid _alphaOwner;
    private Guid _alphaManager;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    private static DateOnly LastMonth => ThisMonth.AddMonths(-1);

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
            WHERE i.Id IN (@alpha, @beta) AND (m.Code = 'GIVING' OR (m.Code = 'BOOKINGS' AND i.Id = @beta));
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
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM auth.UserPermissionOverrides WHERE UserId IN @users;
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users });
        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    /// <summary>The report as it reads on <paramref name="utcNow"/>, through the real calendar and queries.</summary>
    private async Task<MonthlyReportView> ReportAtAsync(DateTime utcNow, int? institutionId = null, DateOnly? month = null)
    {
        var connections = new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform());
        var clock = new FixedClock(utcNow);
        var calendar = new AvailabilityCalendar(new DonorBookingQueries(connections), new SqlStandingHolds(database.ConnectionString), clock);
        var handler = new MonthlyReportHandler(new ReportQueries(connections), calendar, clock);

        var report = await handler.GetAsync(institutionId ?? database.AlphaInstitutionId, month ?? March);
        Assert.NotNull(report);
        return report;
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

    private static string RandomPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    private async Task<Guid> MemberAsync(int institutionId, string role)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'InstitutionStaff', N'Report Test', @phone, 1, 'en', 'Active', 'stamp');

            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @institutionId AND r.Code = @role;
            """,
            new { id, phone = RandomPhone(), institutionId, role });

        _users.Add(id);
        return id;
    }

    private async Task DenyAsync(Guid user, string permission) =>
        await ExecuteAsync(
            """
            INSERT auth.UserPermissionOverrides (UserId, InstitutionId, PermissionId, Effect, Reason, CreatedBy)
            SELECT @user, @alpha, p.Id, 'Deny', N'Test', @user FROM auth.Permissions p WHERE p.Code = @permission;
            UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @user;
            """,
            new { user, alpha = database.AlphaInstitutionId, permission });

    private HttpClient ClientFor(Guid userId, int? institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
    }

    private HttpClient Alpha(Guid member) => ClientFor(member, database.AlphaInstitutionId);

    private HttpClient Beta(Guid member) => ClientFor(member, database.BetaInstitutionId);

    private async Task<HttpClient> SteppedUpAlphaAsync(Guid member)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(member, database.AlphaInstitutionId, IsPlatform: false));
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(member, tokens.Access.JwtId), 1, TimeSpan.FromMinutes(15));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
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

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path) => SendAsync(client, HttpMethod.Get, path);

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>A meal slot; a Shared one, so a day can hold several bookings.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync(
        byte days = MealSlot.EveryDay, DateTime? created = null, bool active = true, int? institutionId = null, string name = "Lunch", string? nameSi = null)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, NameSi, StartTime, EndTime, DaysOfWeekMask, BookingMode, MaxSponsors,
                 MinHeadcount, MaxHeadcount, MinAdvanceDays, MaxAdvanceDays, RequiresApproval, SortOrder, IsActive, CreatedAt, CreatedBy)
            OUTPUT inserted.Id
            VALUES (@publicId, @institutionId, @name, @nameSi, '11:00', '12:30', @days, 'Shared', 5,
                    1, 100, 1, 365, 1, 1, @active, @created, @createdBy);
            """,
            new
            {
                publicId,
                institutionId = institutionId ?? database.AlphaInstitutionId,
                name = $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)],
                nameSi,
                days,
                active,
                created = created ?? LongAgo,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return (id, publicId);
    }

    /// <summary>A booking in any status on any day, straight into the tables: the engine only books ahead.</summary>
    private async Task BookAsync(int slotId, DateOnly date, string status) =>
        await ExecuteAsync(
            """
            DECLARE @institutionId INT = (SELECT InstitutionId FROM booking.MealSlots WHERE Id = @slotId);

            IF NOT EXISTS (SELECT 1 FROM booking.SlotInstances WHERE MealSlotId = @slotId AND SlotDate = @date)
                INSERT booking.SlotInstances (InstitutionId, MealSlotId, SlotDate, MaxSponsors, MaxHeadcount)
                VALUES (@institutionId, @slotId, @date, 5, 100);

            INSERT booking.Bookings (BookingNo, InstitutionId, SlotInstanceId, DonorName, DonorPhone, Headcount, Status, CreatedBy,
                                     RejectReason, CancelledAt)
            SELECT @bookingNo, @institutionId, si.Id, N'Report Donor', @phone, 5, @status, @createdBy,
                   CASE WHEN @status = 'Rejected' THEN N'Test' END,
                   CASE WHEN @status = 'Cancelled' THEN SYSUTCDATETIME() END
            FROM booking.SlotInstances AS si WHERE si.MealSlotId = @slotId AND si.SlotDate = @date;
            """,
            new
            {
                slotId,
                date,
                status,
                bookingNo = "T" + Guid.NewGuid().ToString("N")[..12],
                phone = RandomPhone(),
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

    private async Task BlockAsync(DateOnly date, int? slotId = null) =>
        await ExecuteAsync(
            """
            INSERT booking.BlockedDates (InstitutionId, MealSlotId, DateFrom, DateTo, Reason, CreatedBy)
            VALUES (@alpha, @slotId, @date, @date, N'Test', @createdBy);
            """,
            new { alpha = database.AlphaInstitutionId, slotId, date, createdBy = SethikaTestDatabase.AlphaUserId });

    private async Task<string> RecordAsync(object body)
    {
        var json = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, body));
        return json.GetProperty("receiptNo").GetString()!;
    }

    private static Dictionary<string, object?> Cash(decimal amount, DateOnly date, string method = "CASH", string name = "Walk-in Donor") =>
        new()
        {
            ["kind"] = "Money",
            ["amount"] = amount,
            ["method"] = method,
            ["date"] = Iso(date),
            ["donorName"] = name,
        };

    private static Dictionary<string, object?> Items(string items, DateOnly date, Guid? need = null, decimal? quantity = 5m, decimal? estimate = null) =>
        new()
        {
            ["kind"] = "InKind",
            ["items"] = items,
            ["quantity"] = quantity,
            ["estimatedValue"] = estimate,
            ["date"] = Iso(date),
            ["donorName"] = "Walk-in Donor",
            ["need"] = need,
        };

    private async Task VoidAsync(string receiptNo)
    {
        var version = Convert.ToBase64String(await ScalarAsync<byte[]>(
            "SELECT RowVersion FROM giving.Donations WHERE InstitutionId = @alpha AND ReceiptNo = @receiptNo;",
            new { alpha = database.AlphaInstitutionId, receiptNo }));

        await BodyOf(await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{receiptNo}/void", new { reason = "Recorded twice.", version }));
    }

    /// <summary>A gift written straight in, at an exact instant: the API stores a past day at noon (M23).</summary>
    private async Task GiftAtAsync(DateTime utc, decimal amount) =>
        await ExecuteAsync(
            """
            INSERT giving.Donations (ReceiptNo, InstitutionId, DonorName, Kind, Amount, PaymentMethodLookupId, DonatedAt, RecordedBy, PublicId)
            SELECT @receiptNo, @alpha, N'Edge', 'Money', @amount, v.Id, @utc, @recordedBy, NEWID()
            FROM core.LookupValues AS v
            JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId AND t.Code = 'PAYMENT_METHOD'
            WHERE v.Code = 'CASH';
            """,
            new { receiptNo = "T" + Guid.NewGuid().ToString("N")[..12], alpha = database.AlphaInstitutionId, amount, utc, recordedBy = SethikaTestDatabase.AlphaUserId });

    /// <summary>A need written straight in, with the instants a test wants.</summary>
    private async Task<int> NeedAtAsync(string status, DateTime? opened, DateTime? ended, int? institutionId = null) =>
        await ScalarAsync<int>(
            """
            INSERT giving.Needs (InstitutionId, Title, CategoryLookupId, UnitLookupId, QuantityNeeded, Status, CreatedBy, OpenedAt, EndedAt)
            OUTPUT inserted.Id
            SELECT @institutionId, N'Test need', c.Id, u.Id, 10, @status, @createdBy, @opened, @ended
            FROM core.LookupValues AS c
            JOIN core.LookupTypes AS ct ON ct.Id = c.LookupTypeId AND ct.Code = 'NEED_CATEGORY' AND c.Code = 'STATIONERY'
            CROSS JOIN core.LookupValues AS u
            JOIN core.LookupTypes AS ut ON ut.Id = u.LookupTypeId AND ut.Code = 'UNIT' AND u.Code = 'PIECES';
            """,
            new { institutionId = institutionId ?? database.AlphaInstitutionId, status, createdBy = SethikaTestDatabase.AlphaUserId, opened, ended });

    private async Task<string> NeedVersionAsync(Guid need) =>
        Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Needs WHERE PublicId = @need;", new { need }));

    private async Task<Guid> OpenNeedAsync(decimal quantity = 10m, string category = "STATIONERY")
    {
        var draft = await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Needs, new Dictionary<string, object?>
        {
            ["title"] = "Exercise books",
            ["description"] = null,
            ["category"] = category,
            ["unit"] = "PIECES",
            ["quantity"] = quantity,
            ["priority"] = "Normal",
            ["deadline"] = null,
            ["version"] = null,
        }));
        var need = draft.GetProperty("publicId").GetGuid();
        await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/open", new { version = await NeedVersionAsync(need) }));
        return need;
    }

    private sealed record NeedDates(string Status, DateTime? OpenedAt, DateTime? EndedAt);

    private async Task<NeedDates> NeedDatesAsync(Guid need)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<NeedDates>("SELECT Status, OpenedAt, EndedAt FROM giving.Needs WHERE PublicId = @need;", new { need });
    }

    private static SlotUtilisationView Row(MonthlyReportView report, Guid slot) => report.Utilisation.Single(u => u.MealSlot == slot);

    private static int Count(MonthlyReportView report, string status) => report.Bookings.Single(b => b.Status == status).Count;

    // ------------------------------------------------------------------ utilisation (M16)

    [Fact]
    public async Task Utilisation_IsBookedDaysOverTheDaysTheSlotServed()
    {
        var (weekdays, weekdaysId) = await SlotAsync(Weekdays);
        var (_, dailyId) = await SlotAsync();

        // Monday the 4th is blocked for this slot only.
        await BlockAsync(new DateOnly(2030, 3, 4), weekdays);

        await BookAsync(weekdays, new DateOnly(2030, 3, 5), BookingStatus.Approved);
        await BookAsync(weekdays, new DateOnly(2030, 3, 6), BookingStatus.Completed);
        await BookAsync(weekdays, new DateOnly(2030, 3, 7), BookingStatus.Approved);
        await BookAsync(weekdays, new DateOnly(2030, 3, 7), BookingStatus.Approved);   // one day, however many sponsors
        await BookAsync(weekdays, new DateOnly(2030, 3, 8), BookingStatus.Requested);
        await BookAsync(weekdays, new DateOnly(2030, 3, 11), BookingStatus.Cancelled);
        await BookAsync(weekdays, new DateOnly(2030, 3, 12), BookingStatus.Rejected);
        await BookAsync(weekdays, new DateOnly(2030, 3, 13), BookingStatus.NoShow);
        await BookAsync(weekdays, new DateOnly(2030, 3, 4), BookingStatus.Completed);  // the block came after it
        await BookAsync(weekdays, new DateOnly(2030, 3, 9), BookingStatus.Approved);   // a Saturday, not one of its days
        await BookAsync(weekdays, new DateOnly(2030, 4, 2), BookingStatus.Approved);   // next month

        var report = await ReportAtAsync(AfterMarch);

        var row = Row(report, weekdaysId);
        Assert.Equal(20, row.ServedDays);
        Assert.Equal(3, row.BookedDays);
        Assert.Equal(15, row.Percent);

        var other = Row(report, dailyId);
        Assert.Equal(31, other.ServedDays);
        Assert.Equal(0, other.BookedDays);
        Assert.Equal(0, other.Percent);

        Assert.Equal("2030-03", report.Month);
        Assert.True(report.MonthOver);
        Assert.Equal(
            ["Requested", "Approved", "Completed", "NoShow", "Rejected", "Cancelled"],
            report.Bookings.Select(b => b.Status));
        Assert.Equal(1, Count(report, BookingStatus.Requested));
        Assert.Equal(4, Count(report, BookingStatus.Approved));
        Assert.Equal(2, Count(report, BookingStatus.Completed));
        Assert.Equal(1, Count(report, BookingStatus.NoShow));
        Assert.Equal(1, Count(report, BookingStatus.Rejected));
        Assert.Equal(1, Count(report, BookingStatus.Cancelled));
    }

    [Fact]
    public async Task ABlockOnEverySlot_TakesTheDayFromEachOfThem()
    {
        var (_, slot) = await SlotAsync();
        await BlockAsync(new DateOnly(2030, 3, 10));

        Assert.Equal(30, Row(await ReportAtAsync(AfterMarch), slot).ServedDays);
    }

    [Fact]
    public async Task ASeasonalSlot_ServedOnlyInsideItsSeason()
    {
        var (id, slot) = await SlotAsync();
        await ExecuteAsync(
            "INSERT booking.MealSlotSeasons (MealSlotId, Name, DateFrom, DateTo) VALUES (@id, N'Test', '2030-03-10', '2030-03-17');",
            new { id });
        await BookAsync(id, new DateOnly(2030, 3, 12), BookingStatus.Approved);

        var row = Row(await ReportAtAsync(AfterMarch), slot);

        Assert.Equal(8, row.ServedDays);
        Assert.Equal(13, row.Percent);   // 12.5 rounds up
    }

    [Fact]
    public async Task ASlotMadeInTheMonth_ServedFromThatDayInColombo()
    {
        // 19:00 UTC on the 10th is already 00:30 on the 11th in Colombo.
        var (_, slot) = await SlotAsync(created: new DateTime(2030, 3, 10, 19, 0, 0, DateTimeKind.Utc));

        Assert.Equal(21, Row(await ReportAtAsync(AfterMarch), slot).ServedDays);
    }

    [Fact]
    public async Task AMonthNotOver_CountsUpToToday()
    {
        var (id, slot) = await SlotAsync();
        await BookAsync(id, new DateOnly(2030, 3, 14), BookingStatus.Approved);
        await BookAsync(id, new DateOnly(2030, 3, 15), BookingStatus.Approved);
        await BookAsync(id, new DateOnly(2030, 3, 20), BookingStatus.Approved);   // still to come

        var (retiredId, retired) = await SlotAsync(active: false);
        await BookAsync(retiredId, new DateOnly(2030, 3, 14), BookingStatus.Completed);
        await BookAsync(retiredId, new DateOnly(2030, 3, 20), BookingStatus.Approved);

        var report = await ReportAtAsync(new DateTime(2030, 3, 15, 6, 0, 0, DateTimeKind.Utc));

        var row = Row(report, slot);
        Assert.Equal(15, row.ServedDays);
        Assert.Equal(2, row.BookedDays);
        Assert.Equal(13, row.Percent);
        Assert.False(report.MonthOver);
        Assert.Equal(1, Row(report, retired).BookedDays);

        // The bookings still to come are bookings of the month.
        Assert.Equal(4, Count(report, BookingStatus.Approved));
    }

    [Fact]
    public async Task TheMonthEnds_AtMidnightInColombo()
    {
        Assert.False((await ReportAtAsync(new DateTime(2030, 3, 31, 18, 0, 0, DateTimeKind.Utc))).MonthOver);   // 23:30
        Assert.True((await ReportAtAsync(new DateTime(2030, 3, 31, 19, 0, 0, DateTimeKind.Utc))).MonthOver);    // 00:30
    }

    [Fact]
    public async Task AMonthNotBegun_HasNoServedDays_AndNoPercent()
    {
        var (_, slot) = await SlotAsync();

        var row = Row(await ReportAtAsync(new DateTime(2030, 2, 10, 6, 0, 0, DateTimeKind.Utc)), slot);

        Assert.Equal(0, row.ServedDays);
        Assert.Null(row.Percent);
    }

    [Fact]
    public async Task ARetiredSlot_ShowsItsBookedDays_AndSaysNothingOfServedDays()
    {
        var (booked, bookedSlot) = await SlotAsync(active: false);
        var (_, quietSlot) = await SlotAsync(active: false);
        await BookAsync(booked, new DateOnly(2030, 3, 5), BookingStatus.Completed);

        var report = await ReportAtAsync(AfterMarch);

        var row = Row(report, bookedSlot);
        Assert.Equal(1, row.BookedDays);
        Assert.Null(row.ServedDays);
        Assert.Null(row.Percent);
        Assert.DoesNotContain(report.Utilisation, u => u.MealSlot == quietSlot);
    }

    [Fact]
    public async Task ASuspendedInstitution_StillGetsItsReport_WithoutServedDays()
    {
        var (id, slot) = await SlotAsync();
        await BookAsync(id, new DateOnly(2030, 3, 5), BookingStatus.Completed);
        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Id = @alpha;", new { alpha = database.AlphaInstitutionId });

        var row = Row(await ReportAtAsync(AfterMarch), slot);

        Assert.Equal(1, row.BookedDays);
        Assert.Null(row.ServedDays);
    }

    [Fact]
    public async Task AnotherInstitutionsRows_AreNotInTheReport_EvenReadAsThePlatform()
    {
        var (beta, betaSlot) = await SlotAsync(institutionId: database.BetaInstitutionId);
        await BookAsync(beta, new DateOnly(2030, 3, 5), BookingStatus.Approved);
        await NeedAtAsync("Open", LongAgo, null, database.BetaInstitutionId);
        await ExecuteAsync(
            """
            INSERT giving.Donations (ReceiptNo, InstitutionId, DonorName, Kind, Amount, PaymentMethodLookupId, ItemDescription, DonatedAt, RecordedBy, PublicId,
                                     IsVoided, VoidedBy, VoidReason, VoidedAt)
            SELECT r.ReceiptNo, @beta, N'Beta Donor', r.Kind, r.Amount, CASE WHEN r.Kind = 'Money' THEN v.Id END, r.Items, '2030-03-10T06:00:00', @recordedBy, NEWID(),
                   r.IsVoided, CASE WHEN r.IsVoided = 1 THEN @voidedBy END, CASE WHEN r.IsVoided = 1 THEN N'Test' END, CASE WHEN r.IsVoided = 1 THEN SYSUTCDATETIME() END
            FROM (VALUES ('B1', 'Money', 500.00, NULL, 0), ('B2', 'InKind', NULL, N'Chairs', 0), ('B3', 'Money', 600.00, NULL, 1))
                 AS r (ReceiptNo, Kind, Amount, Items, IsVoided)
            CROSS JOIN core.LookupValues AS v
            JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId AND t.Code = 'PAYMENT_METHOD'
            WHERE v.Code = 'CASH';
            """,
            new { beta = database.BetaInstitutionId, recordedBy = SethikaTestDatabase.AlphaUserId, voidedBy = _betaOwner });

        var report = await ReportAtAsync(AfterMarch);

        Assert.DoesNotContain(report.Utilisation, u => u.MealSlot == betaSlot);
        Assert.Equal(0, Count(report, BookingStatus.Approved));
        Assert.Equal(0, report.Needs.StillOpen);
        Assert.Empty(report.Money);
        Assert.Empty(report.InKind);
        Assert.Empty(report.Voided);

        // And the figures are there for Beta, so the assertions above are not empty by accident.
        var beta2 = await ReportAtAsync(AfterMarch, database.BetaInstitutionId);
        Assert.Equal(500m, beta2.MoneyTotal);
        Assert.Single(beta2.InKind);
        Assert.Single(beta2.Voided);
    }

    // ------------------------------------------------------------------ gifts (M16)

    [Fact]
    public async Task Money_ByMethod_LeavesVoidedGiftsOutAndListsThemApart()
    {
        var day = LastMonth.AddDays(9);
        await RecordAsync(Cash(1000m, day));
        await RecordAsync(Cash(250.50m, day));
        await RecordAsync(Cash(4000m, day, method: "CHEQUE"));
        var voided = await RecordAsync(Cash(9999m, day, name: "Wrong Donor"));
        await RecordAsync(Cash(7m, ThisMonth));
        await VoidAsync(await RecordAsync(Cash(8m, ThisMonth)));
        await VoidAsync(await RecordAsync(Cash(9m, LastMonth.AddDays(-1))));
        await VoidAsync(voided);

        var body = await BodyOf(await GetAsync(Alpha(_alphaOwner), $"{Report}?month={Month(LastMonth)}"));

        var money = body.GetProperty("money").EnumerateArray().ToDictionary(m => m.GetProperty("code").GetString()!);
        Assert.Equal(2, money.Count);
        Assert.Equal(1250.50m, money["CASH"].GetProperty("amount").GetDecimal());
        Assert.Equal(2, money["CASH"].GetProperty("gifts").GetInt32());
        Assert.Equal(4000m, money["CHEQUE"].GetProperty("amount").GetDecimal());
        Assert.Equal(5250.50m, body.GetProperty("moneyTotal").GetDecimal());

        var gift = Assert.Single(body.GetProperty("voided").EnumerateArray());
        Assert.Equal(voided, gift.GetProperty("receiptNo").GetString());
        Assert.Equal("Wrong Donor", gift.GetProperty("donorName").GetString());
        Assert.Equal(9999m, gift.GetProperty("amount").GetDecimal());
        Assert.Equal("Recorded twice.", gift.GetProperty("voidReason").GetString());
        Assert.Equal("Report Test", gift.GetProperty("voidedByName").GetString());
        Assert.EndsWith("Z", gift.GetProperty("voidedAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGift_BelongsToTheColomboMonthItWasGivenIn()
    {
        await GiftAtAsync(new DateTime(2030, 2, 28, 18, 29, 59, DateTimeKind.Utc), 1m);   // 23:59:59 on 28 Feb
        await GiftAtAsync(new DateTime(2030, 2, 28, 18, 30, 0, DateTimeKind.Utc), 10m);   // 00:00:00 on 1 March
        await GiftAtAsync(new DateTime(2030, 3, 31, 18, 29, 59, DateTimeKind.Utc), 100m); // 23:59:59 on 31 March
        await GiftAtAsync(new DateTime(2030, 3, 31, 18, 30, 0, DateTimeKind.Utc), 1000m); // 00:00:00 on 1 April

        Assert.Equal(110m, (await ReportAtAsync(AfterMarch)).MoneyTotal);
    }

    [Fact]
    public async Task GiftsOfItems_GoByTheirNeedsCategory_AndAGiftForNoNeedStandsAlone()
    {
        var books = await OpenNeedAsync(category: "STATIONERY");
        var rice = await OpenNeedAsync(category: "DRY_RATIONS");
        await RecordAsync(Items("Books", Today, books, estimate: 500m));
        await RecordAsync(Items("More books", Today, books, quantity: 2m));
        await RecordAsync(Items("Rice", Today, rice, estimate: 900m));
        await RecordAsync(Items("Old chairs", Today, quantity: null));
        var voided = await RecordAsync(Items("A mistake", Today, rice, quantity: 1m, estimate: 7777m));
        await VoidAsync(voided);

        var body = await BodyOf(await GetAsync(Alpha(_alphaOwner), Report));

        var rows = body.GetProperty("inKind").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);

        var stationery = rows.Single(r => r.GetProperty("code").GetString() == "STATIONERY");
        Assert.Equal(2, stationery.GetProperty("gifts").GetInt32());
        Assert.Equal(500m, stationery.GetProperty("estimatedValue").GetDecimal());

        var rations = rows.Single(r => r.GetProperty("code").GetString() == "DRY_RATIONS");
        Assert.Equal(1, rations.GetProperty("gifts").GetInt32());
        Assert.Equal(900m, rations.GetProperty("estimatedValue").GetDecimal());

        // Gifts for no need come last, and an unestimated gift has no value rather than a value of zero.
        var none = rows[^1];
        Assert.Equal(JsonValueKind.Null, none.GetProperty("code").ValueKind);
        Assert.Equal(1, none.GetProperty("gifts").GetInt32());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("estimatedValue").ValueKind);

        Assert.Single(body.GetProperty("voided").EnumerateArray());
        Assert.Equal(0m, body.GetProperty("moneyTotal").GetDecimal());
    }

    // ------------------------------------------------------------------ needs (M16, M37)

    [Fact]
    public async Task Needs_AreCountedByWhenTheyOpenedAndEnded()
    {
        var feb = new DateTime(2030, 2, 10, 6, 0, 0, DateTimeKind.Utc);
        var mar = new DateTime(2030, 3, 10, 6, 0, 0, DateTimeKind.Utc);
        var apr = new DateTime(2030, 4, 5, 6, 0, 0, DateTimeKind.Utc);

        await NeedAtAsync("Open", feb, null);          // still open
        await NeedAtAsync("Fulfilled", feb, apr);      // still open as March ended
        await NeedAtAsync("Fulfilled", mar, mar);      // opened and fulfilled
        await NeedAtAsync("Closed", mar, mar);         // opened, not fulfilled, not open
        await NeedAtAsync("Open", apr, null);          // nothing to March
        await NeedAtAsync("Draft", null, null);        // nothing
        await NeedAtAsync("Cancelled", null, mar);     // a draft withdrawn: nothing

        var needs = (await ReportAtAsync(AfterMarch)).Needs;

        Assert.Equal(2, needs.Opened);
        Assert.Equal(1, needs.Fulfilled);
        Assert.Equal(2, needs.StillOpen);
    }

    [Fact]
    public async Task ANeedsDates_FollowItThroughTheBoard()
    {
        // Opened, then filled by a gift: it ended. The gift voided: it stands again (M30).
        var need = await OpenNeedAsync(5m);
        var opened = await NeedDatesAsync(need);
        Assert.NotNull(opened.OpenedAt);
        Assert.Null(opened.EndedAt);

        var receipt = await RecordAsync(Items("Books", Today, need, quantity: 5m));
        var filled = await NeedDatesAsync(need);
        Assert.Equal("Fulfilled", filled.Status);
        Assert.NotNull(filled.EndedAt);

        var report = await BodyOf(await GetAsync(Alpha(_alphaOwner), Report));
        Assert.Equal(1, report.GetProperty("needs").GetProperty("fulfilled").GetInt32());
        Assert.Equal(0, report.GetProperty("needs").GetProperty("stillOpen").GetInt32());

        await VoidAsync(receipt);
        var reopened = await NeedDatesAsync(need);
        Assert.Equal("Open", reopened.Status);
        Assert.Null(reopened.EndedAt);
        Assert.Equal(opened.OpenedAt, reopened.OpenedAt);

        // A partial gift leaves it standing; closing ends it, and voiding that gift leaves it closed.
        var partial = await RecordAsync(Items("Books", Today, need, quantity: 2m));
        Assert.Null((await NeedDatesAsync(need)).EndedAt);

        await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{need}/close", new { version = await NeedVersionAsync(need) }));
        var closed = await NeedDatesAsync(need);
        Assert.NotNull(closed.EndedAt);

        await VoidAsync(partial);
        Assert.Equal(closed, await NeedDatesAsync(need));

        // Cancelling ends one too.
        var other = await OpenNeedAsync();
        await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Needs}/{other}/cancel", new { version = await NeedVersionAsync(other) }));
        Assert.NotNull((await NeedDatesAsync(other)).EndedAt);
    }

    [Fact]
    public async Task ANeedBroughtDownToWhatArrived_EndsThere()
    {
        var need = await OpenNeedAsync(10m);
        await RecordAsync(Items("Books", Today, need, quantity: 4m));
        Assert.Null((await NeedDatesAsync(need)).EndedAt);

        await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Needs}/{need}", new Dictionary<string, object?>
        {
            ["title"] = "Exercise books",
            ["description"] = null,
            ["category"] = "STATIONERY",
            ["unit"] = "PIECES",
            ["quantity"] = 4m,
            ["priority"] = "Normal",
            ["deadline"] = null,
            ["version"] = await NeedVersionAsync(need),
        }));

        var done = await NeedDatesAsync(need);
        Assert.Equal("Fulfilled", done.Status);
        Assert.NotNull(done.EndedAt);
    }

    [Fact]
    public async Task APledgeDeliveredInFull_EndsTheNeed_AndOneDeliveredInPart_DoesNot()
    {
        var need = await OpenNeedAsync(10m);
        var donor = _alphaStaff;
        var id = await ScalarAsync<int>("SELECT Id FROM giving.Needs WHERE PublicId = @need;", new { need });
        async Task<Guid> PledgeAsync(decimal quantity)
        {
            var publicId = Guid.CreateVersion7();
            await ExecuteAsync(
                """
                INSERT giving.Pledges (InstitutionId, NeedId, DonorUserId, Quantity, Status, PublicId) VALUES (@alpha, @id, @donor, @quantity, 'Pledged', @publicId);
                UPDATE giving.Needs SET QuantityPledged += @quantity WHERE Id = @id;
                """,
                new { alpha = database.AlphaInstitutionId, id, donor, quantity, publicId });
            return publicId;
        }

        async Task ReceiveAsync(Guid pledge, decimal quantity)
        {
            var version = Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Pledges WHERE PublicId = @pledge;", new { pledge }));
            await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/pledges/{pledge}/receive", new { quantity, version }));
        }

        await ReceiveAsync(await PledgeAsync(4m), 4m);
        Assert.Null((await NeedDatesAsync(need)).EndedAt);

        await ReceiveAsync(await PledgeAsync(6m), 6m);
        var done = await NeedDatesAsync(need);
        Assert.Equal("Fulfilled", done.Status);
        Assert.NotNull(done.EndedAt);
    }

    // ------------------------------------------------------------------ over HTTP

    [Fact]
    public async Task TheReport_ReadsOnlyTheCallersInstitution()
    {
        await RecordAsync(Cash(1000m, Today));
        await NeedAtAsync("Open", DateTime.UtcNow.AddDays(-40), null);

        var beta = await BodyOf(await GetAsync(Beta(_betaOwner), Report));

        Assert.Empty(beta.GetProperty("money").EnumerateArray());
        Assert.Equal(0m, beta.GetProperty("moneyTotal").GetDecimal());
        Assert.Equal(0, beta.GetProperty("needs").GetProperty("stillOpen").GetInt32());
    }

    [Fact]
    public async Task TheReport_NeedsItsPermission_AndTheFileItsOwn()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaStaff), Report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaStaff), $"{Report}/csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaStaff), $"{Donations}/csv")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Alpha(_alphaManager), Report)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Alpha(_alphaManager), $"{Report}/csv")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Alpha(_alphaManager), $"{Donations}/csv")).StatusCode);

        // Reading the report does not give the file: the deny takes EXPORT alone.
        await DenyAsync(_alphaOwner, "GIVING.REPORTS.EXPORT");
        await DenyAsync(_alphaOwner, "GIVING.DONATIONS.EXPORT");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Alpha(_alphaOwner), Report)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Alpha(_alphaOwner), Donations)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaOwner), $"{Report}/csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaOwner), $"{Donations}/csv")).StatusCode);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("nonsense")]
    [InlineData("0001-01")]
    [InlineData("9999-12")]
    public async Task AMonthThatIsNotOne_IsRefused(string month)
    {
        foreach (var path in new[] { Report, $"{Report}/csv", Donations, $"{Donations}/csv" })
        {
            var response = await GetAsync(Alpha(_alphaOwner), $"{path}?month={month}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("invalid_month", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ the files (M17, M27)

    private static async Task<string> CsvOf(HttpResponseMessage response, string fileName)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public async Task TheReportsFile_HoldsTheSameFigures_WithSinhalaNamesIntact()
    {
        var (id, _) = await SlotAsync(created: DateTime.UtcNow.AddYears(-2), name: "Lunch", nameSi: "දිවා දානය");
        await BookAsync(id, LastMonth.AddDays(2), BookingStatus.Completed);
        var day = LastMonth.AddDays(9);
        await RecordAsync(Cash(1234.50m, day));
        var voided = await RecordAsync(Cash(50m, day, name: "සුනිල් පෙරේරා"));
        await VoidAsync(voided);

        var si = await CsvOf(await GetAsync(Alpha(_alphaOwner), $"{Report}/csv?month={Month(LastMonth)}&lang=si"), $"sethika-report-{Month(LastMonth)}.csv");
        Assert.Contains("දිවා දානය,1,", si, StringComparison.Ordinal);
        Assert.Contains("සුනිල් පෙරේරා", si, StringComparison.Ordinal);
        Assert.Contains("මුදල්", si, StringComparison.Ordinal);
        Assert.Contains(",1,1234.50\r\n", si, StringComparison.Ordinal);
        Assert.Contains("Total,,1234.50\r\n", si, StringComparison.Ordinal);
        Assert.Contains("Completed,1\r\n", si, StringComparison.Ordinal);
        Assert.Contains(voided, si, StringComparison.Ordinal);

        var en = await CsvOf(await GetAsync(Alpha(_alphaOwner), $"{Report}/csv?month={Month(LastMonth)}"), $"sethika-report-{Month(LastMonth)}.csv");
        Assert.DoesNotContain("දිවා දානය", en, StringComparison.Ordinal);
        Assert.Contains("Cash,1,1234.50\r\n", en, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDonationsFile_ListsTheMonth_MarksVoidedGifts_AndDefusesFormulas()
    {
        var day = LastMonth.AddDays(4);
        var kept = await RecordAsync(Cash(100m, day, name: "=HYPERLINK(\"http://evil\",\"x\")"));
        var voided = await RecordAsync(Cash(200m, day, method: "CHEQUE"));
        var items = await RecordAsync(Items("Books, pencils", day, quantity: 2.5m));
        var other = await RecordAsync(Cash(300m, ThisMonth));
        await VoidAsync(voided);

        var csv = await CsvOf(await GetAsync(Alpha(_alphaOwner), $"{Donations}/csv?month={Month(LastMonth)}&lang=si"), $"sethika-donations-{Month(LastMonth)}.csv");
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("Receipt,Date,Donor,Kind,", lines[0], StringComparison.Ordinal);

        var first = lines.Single(l => l.StartsWith(kept, StringComparison.Ordinal));
        Assert.Contains($",{Iso(day)},\"'=HYPERLINK(\"\"http://evil\"\",\"\"x\"\")\",Money,100.00,මුදල්,", first, StringComparison.Ordinal);
        Assert.EndsWith(",No,", first, StringComparison.Ordinal);

        Assert.Contains(",Yes,", lines.Single(l => l.StartsWith(voided, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("\"Books, pencils\",2.5,", lines.Single(l => l.StartsWith(items, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.DoesNotContain(other, csv, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the Day Ledger's giving line (M18)

    [Fact]
    public async Task TheGivingLine_CountsThisMonth_OpenNeeds_AndPledgesDue()
    {
        await RecordAsync(Cash(1000m, Today));
        await RecordAsync(Cash(250m, Today));
        await RecordAsync(Cash(5000m, LastMonth.AddDays(3)));
        await VoidAsync(await RecordAsync(Cash(9000m, Today)));

        var open = await NeedAtAsync("Open", DateTime.UtcNow, null);
        await NeedAtAsync("Open", DateTime.UtcNow, null);
        await NeedAtAsync("Draft", null, null);
        await NeedAtAsync("Fulfilled", DateTime.UtcNow, DateTime.UtcNow);
        await NeedAtAsync("Open", DateTime.UtcNow, null, database.BetaInstitutionId);

        async Task PledgeAsync(DateOnly? expected, string status = "Pledged") =>
            await ExecuteAsync(
                """
                INSERT giving.Pledges (InstitutionId, NeedId, DonorUserId, Quantity, ExpectedDeliveryDate, Status, PublicId,
                                       ReceivedBy, ReceivedAt, ReceivedQuantity)
                VALUES (@alpha, @open, @donor, 1, @expected, @status, NEWID(),
                        CASE WHEN @status = 'Delivered' THEN @donor END,
                        CASE WHEN @status = 'Delivered' THEN SYSUTCDATETIME() END,
                        CASE WHEN @status = 'Delivered' THEN 1 END);
                """,
                new { alpha = database.AlphaInstitutionId, open, donor = _alphaStaff, expected, status });

        await PledgeAsync(Today);
        await PledgeAsync(Today.AddDays(6));
        await PledgeAsync(Today.AddDays(-3));               // overdue, and still owed
        await PledgeAsync(Today.AddDays(7));                // next week
        await PledgeAsync(null);                            // no date given
        await PledgeAsync(Today.AddDays(1), "Delivered");   // settled

        var summary = await BodyOf(await GetAsync(Alpha(_alphaStaff), Summary));

        Assert.Equal(1250m, summary.GetProperty("money").GetDecimal());
        Assert.Equal(2, summary.GetProperty("openNeeds").GetInt32());
        Assert.Equal(3, summary.GetProperty("pledgesDue").GetInt32());

        var beta = await BodyOf(await GetAsync(Beta(_betaOwner), Summary));
        Assert.Equal(0m, beta.GetProperty("money").GetDecimal());
        Assert.Equal(1, beta.GetProperty("openNeeds").GetInt32());
        Assert.Equal(0, beta.GetProperty("pledgesDue").GetInt32());

        // RLS did that; the query names the institution too, which a platform connection shows.
        var queries = new ReportQueries(new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform()));
        Assert.Equal(new GivingSummaryView(0m, 1, 0), await queries.GetGivingSummaryAsync(database.BetaInstitutionId, Today));
    }

    [Fact]
    public async Task TheGivingLine_NeedsDonationsView()
    {
        await DenyAsync(_alphaManager, "GIVING.DONATIONS.VIEW");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Alpha(_alphaManager), Summary)).StatusCode);
    }
}

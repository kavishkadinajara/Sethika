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
using Sethika.Application.Bookings.Memorial;
using Sethika.Application.Bookings.Recurring;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 07, C.2: the Memorial Dana Planner against real SQL Server - the draft seeds, a plan's
/// observances from the rules, a booking for an observance through the real engine, a yearly one the
/// yearly job keeps booking, the week-ahead reminder through the seeded templates, and Row-Level
/// Security that keeps a plan its donor's alone.
/// </summary>
/// <remarks>
/// Dates are worked out from the rules as the planner works them out, never typed, so the tests read
/// the same on any day they run. The reminder and the yearly job run on fixed clocks.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class MemorialPlanTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "memorial-plan-test-signing-key-at-least-32-bytes";
    private const string SeventhDay = "BUDDHIST_7TH_DAY";
    private const string ThreeMonths = "BUDDHIST_3_MONTHS";
    private const string Yearly = "BUDDHIST_YEARLY";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;", new { });

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

    private SqlConnectionFactory ConnectionsFor(ICurrentUserContext user) => new(database.ConnectionString, user);

    /// <summary>18:00 in Colombo on <paramref name="today"/>, when the reminder runs.</summary>
    private static FixedTimeProvider ClockAt(DateOnly today) => new(SriLankaTime.ToUtc(today, new TimeOnly(18, 0)));

    private static BookMealHandler Engine(SethikaDbContext context, TimeProvider clock) =>
        new(context, new NoLocks(), new SqlSlotInventory(context), new SqlNumberSeries(context), clock);

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<(Guid Id, string Phone)> DonorAsync(string language = "en")
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Memorial Donor', @phone, 1, @language, 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone, language });

        return (id, phone);
    }

    /// <summary>A lunch at Alpha, every day, approval needed, a 1-60 day window.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync()
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @alpha, @name, '11:30', '13:00', 'Shared', 10, 1, 200, 1, 60, 1, 'DonorBrings', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId,
                alpha = database.AlphaInstitutionId,
                name = "Memorial " + Guid.NewGuid().ToString("N")[..8],
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return (id, publicId);
    }

    /// <summary>A plan of <paramref name="donor"/>'s, made through the handler as the donor.</summary>
    private async Task<(int Id, Guid PublicId)> PlanAsync(Guid donor, DateOnly dateOfPassing, string religion = "BUDDHISM")
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var handler = new MemorialPlanHandler(context, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor))), TimeProvider.System);
        var result = await handler.CreateAsync(donor, new CreateMemorialPlanCommand("Amma", "MOTHER", dateOfPassing, religion, null));
        Assert.True(result.Succeeded, result.Error);

        var id = await ScalarAsync<int>("SELECT Id FROM booking.MemorialPlans WHERE PublicId = @plan;", new { plan = result.Plan });
        return (id, result.Plan!.Value);
    }

    private async Task<MemorialRule> RuleAsync(string code)
    {
        await using var context = ContextFor(TestCurrentUserContext.Platform());
        return await context.MemorialRules.AsNoTracking().SingleAsync(r => r.Code == code);
    }

    private async Task<DateOnly> DayOfAsync(DateOnly dateOfPassing, string rule, int number = 1) =>
        MemorialObservances.Date(dateOfPassing, await RuleAsync(rule), number);

    /// <summary>A donor's booking through the engine on <paramref name="today"/>, for an observance when a plan is given.</summary>
    private async Task<BookMealResult> BookAsync(
        Guid donor, Guid slot, DateOnly date, DateOnly today, Guid? plan = null, string? rule = null, bool everyYear = false)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        return await Engine(context, ClockAt(today))
            .BookAsync(donor, new BookMealCommand(slot, date, 10, FulfilmentMode.DonorBrings, "Amma", null, everyYear, plan, rule));
    }

    private MemorialPlanner PlannerFor(Guid donor, DateOnly today) =>
        new(new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor))), ClockAt(today));

    private MemorialReminders RemindersAt(DateOnly today)
    {
        var connections = ConnectionsFor(TestCurrentUserContext.Platform());
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        return new MemorialReminders(new MemorialQueries(connections), new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance), ClockAt(today));
    }

    private sealed record Sent(string Body, string DedupKey);

    private async Task<List<Sent>> SentToAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<Sent>(
            "SELECT Body AS Body, DedupKey AS DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;",
            new { phone })];
    }

    private async Task<string> ExpectedAsync(string language, DateOnly date, string observance)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var body = await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @template AND Channel = 'SMS' AND Language = @language;",
            new { template = MemorialReminders.Template, language }) ?? throw new InvalidOperationException($"MEMORIAL_REMINDER {language} is not seeded.");

        return body.Replace("{date}", Iso(date), StringComparison.Ordinal).Replace("{observance}", observance, StringComparison.Ordinal);
    }

    private sealed record BookingRow(string Source, int? MemorialPlanId, short? MemorialRuleId, int? RecurringBookingId, DateTime SlotDate);

    private async Task<List<BookingRow>> BookingsOfAsync(int slotId)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            """
            SELECT b.Source, b.MemorialPlanId, b.MemorialRuleId, b.RecurringBookingId, si.SlotDate
            FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId
            WHERE si.MealSlotId = @slotId ORDER BY si.SlotDate;
            """,
            new { slotId })];
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private HttpClient ClientFor(Guid userId, int? institutionId = null)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.8.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
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

    private static JsonElement Observance(JsonElement plan, string rule) =>
        plan.GetProperty("observances").EnumerateArray().Single(o => o.GetProperty("rule").GetString() == rule);

    // ------------------------------------------------------------------ the seeds and the form

    [Fact]
    public async Task TheFormOffersTheDraftFaithsWithTheirObservances_AndTheRelationships()
    {
        var (donor, _) = await DonorAsync();

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/memorial/religions"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var religions = (await BodyOf(response)).EnumerateArray().ToList();
            Assert.Equal(["BUDDHISM", "HINDUISM", "ISLAM", "CHRISTIANITY"], religions.Select(r => r.GetProperty("code").GetString()));

            var buddhism = religions[0].GetProperty("rules").EnumerateArray()
                .Select(r => (r.GetProperty("code").GetString(), r.GetProperty("offsetMonths").GetInt32(), r.GetProperty("offsetDays").GetInt32(), r.GetProperty("repeatsAnnually").GetBoolean()));
            Assert.Equal([(SeventhDay, 0, 6, false), (ThreeMonths, 3, 0, false), (Yearly, 12, 0, true)], buddhism);

            // Every rule says it is a draft, in all three languages (H7).
            var names = religions.SelectMany(r => r.GetProperty("rules").EnumerateArray())
                .SelectMany(r => new[] { r.GetProperty("name").GetString(), r.GetProperty("nameSi").GetString(), r.GetProperty("nameTa").GetString() })
                .ToList();
            Assert.Equal(18, names.Count);
            Assert.All(names, n => Assert.StartsWith("DRAFT - ", n, StringComparison.Ordinal));
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/memorial/relationships"))
        {
            var relationships = (await BodyOf(response)).EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
            Assert.Equal(12, relationships.Count);
            Assert.Equal(["MOTHER", "FATHER"], relationships.Take(2));
        }

        // The donor's screens: a session switched into an institution is refused.
        using (var response = await SendAsync(ClientFor(donor, database.AlphaInstitutionId), HttpMethod.Get, "/api/memorial/religions"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task ADonorMakesAPlan_AndSeesEachObservanceOnItsDay()
    {
        var (donor, _) = await DonorAsync();
        var passing = Today.AddDays(-3);

        Guid plan;
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/memorial/plans",
            new { deceasedName = "  Amma  ", relationship = "MOTHER", dateOfPassing = Iso(passing), religion = "BUDDHISM", notes = "Her favourite was kiribath" }))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            plan = (await BodyOf(response)).GetProperty("plan").GetGuid();
        }

        var expected = new[]
        {
            (SeventhDay, 1, Iso(await DayOfAsync(passing, SeventhDay)), "toBook"),
            (ThreeMonths, 1, Iso(await DayOfAsync(passing, ThreeMonths)), "toBook"),
            (Yearly, 1, Iso(await DayOfAsync(passing, Yearly)), "toBook"),
        };

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/memorial/plans"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var only = Assert.Single((await BodyOf(response)).EnumerateArray());
            Assert.Equal(
                (plan, "Amma", "MOTHER", Iso(passing), "BUDDHISM", "Her favourite was kiribath"),
                (only.GetProperty("publicId").GetGuid(), only.GetProperty("deceasedName").GetString(), only.GetProperty("relationship").GetString(),
                 only.GetProperty("dateOfPassing").GetString(), only.GetProperty("religion").GetString(), only.GetProperty("notes").GetString()));
            Assert.Equal(
                expected,
                only.GetProperty("observances").EnumerateArray()
                    .Select(o => (o.GetProperty("rule").GetString()!, o.GetProperty("number").GetInt32(), o.GetProperty("date").GetString()!, o.GetProperty("state").GetString()!)));
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, $"/api/memorial/plans/{plan}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var one = await BodyOf(response);
            Assert.Equal(plan, one.GetProperty("publicId").GetGuid());
            Assert.Equal(3, one.GetProperty("observances").GetArrayLength());
        }

        // A Hindu plan has the 31st day only: past the day after, still to book on the day itself.
        var (_, hindu) = await PlanAsync(donor, Today.AddDays(-31), "HINDUISM");
        var day = Assert.Single((await PlannerFor(donor, Today).GetAsync(hindu, donor))!.Observances);
        Assert.Equal(("HINDU_31ST_DAY", Today.AddDays(-1), ObservanceState.Past), (day.Rule, day.Date, day.State));

        var (_, today) = await PlanAsync(donor, Today.AddDays(-30), "HINDUISM");
        day = Assert.Single((await PlannerFor(donor, Today).GetAsync(today, donor))!.Observances);
        Assert.Equal((Today, ObservanceState.ToBook), (day.Date, day.State));
    }

    [Fact]
    public async Task AFaithWithNoActiveObservanceIsNeitherOfferedNorTaken()
    {
        var (donor, _) = await DonorAsync();
        await ExecuteAsync("UPDATE core.MemorialRules SET IsActive = 0 WHERE Code = 'CHRISTIAN_40TH_DAY';", new { });
        try
        {
            await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
            var queries = new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor)));

            Assert.Equal(["BUDDHISM", "HINDUISM", "ISLAM"], (await queries.ListReligionsAsync()).Select(r => r.Code));

            var result = await new MemorialPlanHandler(context, queries, TimeProvider.System)
                .CreateAsync(donor, new CreateMemorialPlanCommand("Thaththa", null, Today, "CHRISTIANITY", null));
            Assert.Equal(["religion"], result.Fields);
        }
        finally
        {
            await ExecuteAsync("UPDATE core.MemorialRules SET IsActive = 1 WHERE Code = 'CHRISTIAN_40TH_DAY';", new { });
        }
    }

    [Fact]
    public async Task APlanIsRefusedWithTheFieldsToFix()
    {
        var (donor, _) = await DonorAsync();

        async Task<IReadOnlyList<string>> Refused(string? name, string? relationship, DateOnly passing, string? religion, string? notes = null)
        {
            await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
            var handler = new MemorialPlanHandler(context, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor))), TimeProvider.System);
            var result = await handler.CreateAsync(donor, new CreateMemorialPlanCommand(name, relationship, passing, religion, notes));
            Assert.Equal(MemorialPlanResult.InvalidDetails, result.Error);
            return result.Fields;
        }

        Assert.Equal(["deceasedName"], await Refused("  ", null, Today, "BUDDHISM"));
        Assert.Equal(["deceasedName"], await Refused(new string('a', MemorialPlan.MaxDeceasedName + 1), null, Today, "BUDDHISM"));
        Assert.Equal(["dateOfPassing"], await Refused("Amma", null, Today.AddDays(1), "BUDDHISM"));
        Assert.Equal(["dateOfPassing"], await Refused("Amma", null, new DateOnly(1899, 12, 31), "BUDDHISM"));
        Assert.Equal(["notes"], await Refused("Amma", null, Today, "BUDDHISM", new string('n', MemorialPlan.MaxNotes + 1)));
        Assert.Equal(["religion"], await Refused("Amma", null, Today, null));
        Assert.Equal(["religion"], await Refused("Amma", null, Today, "MOTHER"));
        Assert.Equal(["relationship"], await Refused("Amma", "BUDDHISM", Today, "BUDDHISM"));

        // The limits themselves are taken: the oldest date, today, and the longest name.
        await using (var context = ContextFor(TestCurrentUserContext.ForUser(donor)))
        {
            var handler = new MemorialPlanHandler(context, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor))), TimeProvider.System);
            Assert.True((await handler.CreateAsync(donor, new CreateMemorialPlanCommand(new string('a', MemorialPlan.MaxDeceasedName), null, MemorialPlanHandler.EarliestDateOfPassing, "ISLAM", null))).Succeeded);
            Assert.True((await handler.CreateAsync(donor, new CreateMemorialPlanCommand("Thaththa", "FATHER", Today, "CHRISTIANITY", new string('n', MemorialPlan.MaxNotes)))).Succeeded);
        }

        using var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/memorial/plans",
            new { deceasedName = "Amma", dateOfPassing = "01/11/2026", religion = "BUDDHISM" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["dateOfPassing"], (await BodyOf(response)).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task ANewRuleRowChangesAPlansDays_WithNoChangeToTheCode()
    {
        var (donor, _) = await DonorAsync();
        var passing = Today.AddDays(-2);
        var (_, plan) = await PlanAsync(donor, passing);

        await ExecuteAsync(
            """
            INSERT core.MemorialRules (ReligionLookupId, Code, Name, OffsetDays, OffsetMonths, RepeatsAnnually, SortOrder)
            SELECT v.Id, 'TEST_14TH_DAY', N'Test 14th day', 13, 0, 0, 15
            FROM core.LookupValues v JOIN core.LookupTypes t ON t.Id = v.LookupTypeId
            WHERE t.Code = 'RELIGION' AND v.Code = 'BUDDHISM';
            """,
            new { });

        var view = await PlannerFor(donor, Today).GetAsync(plan, donor);
        Assert.Equal(passing.AddDays(13), Assert.Single(view!.Observances, o => o.Rule == "TEST_14TH_DAY").Date);
        Assert.Equal(4, view.Observances.Count);

        // Switched off, it is gone again, and cannot be booked for.
        await ExecuteAsync("UPDATE core.MemorialRules SET IsActive = 0 WHERE Code = 'TEST_14TH_DAY';", new { });
        Assert.Equal(3, (await PlannerFor(donor, Today).GetAsync(plan, donor))!.Observances.Count);

        var (_, slot) = await SlotAsync();
        Assert.Equal(BookMealResult.MemorialNotFound, (await BookAsync(donor, slot, passing.AddDays(13), Today, plan, "TEST_14TH_DAY")).Error);
    }

    // ------------------------------------------------------------------ booking an observance

    [Fact]
    public async Task ABookingForAnObservanceCarriesThePlan_AndThePlanShowsItBooked()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var passing = Today.AddDays(-3);
        var (planId, plan) = await PlanAsync(donor, passing);
        var day = await DayOfAsync(passing, SeventhDay);

        string bookingNo;
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(day), headcount = 10, fulfilment = "DonorBrings", inMemoryOf = "Amma", memorialPlan = plan, memorialRule = SeventhDay }))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            bookingNo = (await BodyOf(response)).GetProperty("bookingNo").GetString()!;
        }

        var rule = await RuleAsync(SeventhDay);
        var booking = Assert.Single(await BookingsOfAsync(slotId));
        Assert.Equal((Booking.MemorialSource, (int?)planId, (short?)rule.Id, (int?)null), (booking.Source, booking.MemorialPlanId, booking.MemorialRuleId, booking.RecurringBookingId));

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, $"/api/memorial/plans/{plan}"))
        {
            var body = await BodyOf(response);
            var seventh = Observance(body, SeventhDay);
            Assert.Equal(
                ("booked", bookingNo, "alpha-temple"),
                (seventh.GetProperty("state").GetString(), seventh.GetProperty("bookingNo").GetString(), seventh.GetProperty("institution").GetString()));

            // Only the observance on the booking's day.
            Assert.Equal(
                ("toBook", JsonValueKind.Null),
                (Observance(body, ThreeMonths).GetProperty("state").GetString(), Observance(body, ThreeMonths).GetProperty("bookingNo").ValueKind));
        }

        // Cancelled, the observance is to book again.
        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(ObservanceState.ToBook, (await PlannerFor(donor, Today).GetAsync(plan, donor))!.Observances.Single(o => o.Rule == SeventhDay).State);
    }

    [Fact]
    public async Task ABookingForAnObservanceIsRefused_OffItsDay_ForAPlanNotTheDonors_OrForAnotherFaithsObservance()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var passing = Today.AddDays(-3);
        var (_, plan) = await PlanAsync(donor, passing);
        var (_, othersPlan) = await PlanAsync(other, passing);
        var (_, archived) = await PlanAsync(donor, passing);
        var day = await DayOfAsync(passing, SeventhDay);

        await using (var context = ContextFor(TestCurrentUserContext.ForUser(donor)))
        {
            Assert.Null(await new MemorialPlanHandler(context, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(donor))), TimeProvider.System).ArchiveAsync(archived, donor));
        }

        Assert.Equal(BookMealResult.NotAnObservance, (await BookAsync(donor, slot, day.AddDays(1), Today, plan, SeventhDay)).Error);
        Assert.Equal(BookMealResult.MemorialNotFound, (await BookAsync(donor, slot, day, Today, othersPlan, SeventhDay)).Error);
        Assert.Equal(BookMealResult.MemorialNotFound, (await BookAsync(donor, slot, day, Today, archived, SeventhDay)).Error);
        Assert.Equal(BookMealResult.MemorialNotFound, (await BookAsync(donor, slot, day, Today, plan, "HINDU_31ST_DAY")).Error);
        Assert.Equal(BookMealResult.MemorialNotFound, (await BookAsync(donor, slot, day, Today, plan, "NO_SUCH_RULE")).Error);
        Assert.Equal(["memorialRule"], (await BookAsync(donor, slot, day, Today, plan, "  ")).Fields);
        Assert.Equal(["memorialPlan"], (await BookAsync(donor, slot, day, Today, null, SeventhDay)).Fields);

        // A 7th day comes once: it is never booked every year.
        Assert.Equal(["everyYear"], (await BookAsync(donor, slot, day, Today, plan, SeventhDay, everyYear: true)).Fields);

        // Row-Level Security aside (the platform sees every plan), another donor's plan is still refused.
        await using (var platform = ContextFor(TestCurrentUserContext.Platform()))
        {
            var refused = await Engine(platform, ClockAt(Today))
                .BookAsync(donor, new BookMealCommand(slot, day, 10, FulfilmentMode.DonorBrings, null, null, false, othersPlan, SeventhDay));
            Assert.Equal(BookMealResult.MemorialNotFound, refused.Error);
        }

        // Nothing was booked and no place was taken.
        Assert.Empty(await BookingsOfAsync(slotId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT ISNULL(SUM(BookedSponsors), 0) FROM booking.SlotInstances WHERE MealSlotId = @slotId;", new { slotId }));

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(day.AddDays(1)), headcount = 10, fulfilment = "DonorBrings", memorialPlan = plan, memorialRule = SeventhDay }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(BookMealResult.NotAnObservance, (await BodyOf(response)).GetProperty("code").GetString());
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings",
            new { mealSlot = slot, date = Iso(day), headcount = 10, fulfilment = "DonorBrings", memorialPlan = othersPlan, memorialRule = SeventhDay }))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(BookMealResult.MemorialNotFound, (await BodyOf(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task EveryYearFromTheYearlyObservance_StartsAYearlyBookingForThePlan_WhichTheJobKeepsBookingForIt()
    {
        var (donor, _) = await DonorAsync();
        var (slotId, slot) = await SlotAsync();
        var passing = Today.AddDays(10).AddYears(-1);
        var (planId, plan) = await PlanAsync(donor, passing);
        var first = await DayOfAsync(passing, Yearly);

        Assert.True((await BookAsync(donor, slot, first, Today, plan, Yearly, everyYear: true)).Succeeded);

        var yearly = await ScalarAsync<int>("SELECT Id FROM booking.RecurringBookings WHERE MemorialPlanId = @planId AND IsActive = 1;", new { planId });

        // Next year, the job books the day for the same plan.
        var second = await DayOfAsync(passing, Yearly, 2);
        var jobDay = second.AddDays(-60);
        await using (var context = ContextFor(TestCurrentUserContext.Platform()))
        {
            var clock = new FixedTimeProvider(SriLankaTime.ToUtc(jobDay, new TimeOnly(0, 30)));
            Assert.Equal(1, (await new RecurringBookingRun(context, Engine(context, clock), clock).RunAsync()).Booked);
        }

        Assert.Equal(
            [(Booking.MemorialSource, (int?)planId, (int?)yearly, first), (Booking.RecurringSource, (int?)planId, (int?)yearly, second)],
            (await BookingsOfAsync(slotId)).Select(b => (b.Source, b.MemorialPlanId, b.RecurringBookingId, DateOnly.FromDateTime(b.SlotDate))));

        // That day, the planner shows next year's observance booked, every year.
        var observance = (await PlannerFor(donor, jobDay).GetAsync(plan, donor))!.Observances.Single(o => o.Rule == Yearly);
        Assert.Equal((2, second, ObservanceState.Booked, true), (observance.Number, observance.Date, observance.State, observance.EveryYear));
    }

    // ------------------------------------------------------------------ the reminder

    [Fact]
    public async Task TheReminderTextsTheDonorOnceAWeekBefore_InTheirLanguage_ButNotForABookedObservanceOrAnArchivedPlan()
    {
        var (donor, phone) = await DonorAsync();
        var (sinhala, sinhalaPhone) = await DonorAsync("si");
        var (booked, bookedPhone) = await DonorAsync();
        var (cancelling, cancelledPhone) = await DonorAsync();
        var (archiving, archivedPhone) = await DonorAsync();
        var (_, slot) = await SlotAsync();

        // Three months on falls about a month from today; the 7th day is long past and the year far off.
        var passing = Today.AddDays(30).AddMonths(-3);
        var day = await DayOfAsync(passing, ThreeMonths);
        var (_, plan) = await PlanAsync(donor, passing);
        await PlanAsync(sinhala, passing);
        var (_, bookedPlan) = await PlanAsync(booked, passing);
        var (cancelledPlanId, cancelledPlan) = await PlanAsync(cancelling, passing);
        var (_, archived) = await PlanAsync(archiving, passing);

        Assert.True((await BookAsync(booked, slot, day, day.AddDays(-20), bookedPlan, ThreeMonths)).Succeeded);

        // A booking cancelled is no booking: that donor is reminded.
        Assert.True((await BookAsync(cancelling, slot, day, day.AddDays(-20), cancelledPlan, ThreeMonths)).Succeeded);
        await ExecuteAsync(
            "UPDATE booking.Bookings SET Status = 'Cancelled', CancelledAt = SYSUTCDATETIME() WHERE MemorialPlanId = @cancelledPlanId;",
            new { cancelledPlanId });

        await using (var context = ContextFor(TestCurrentUserContext.ForUser(archiving)))
        {
            Assert.Null(await new MemorialPlanHandler(context, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(archiving))), TimeProvider.System).ArchiveAsync(archived, archiving));
        }

        // Eight days before: not yet.
        Assert.Equal(0, (await RemindersAt(day.AddDays(-8)).SendAsync()).Due);
        Assert.Empty(await SentToAsync(phone));

        // A week before: the three plans with nothing booked, each in its donor's language.
        var run = await RemindersAt(day.AddDays(-7)).SendAsync();
        Assert.Equal((3, 3, 0), (run.Due, run.Sent, run.NoPhone));
        Assert.Single(await SentToAsync(cancelledPhone));

        var sent = Assert.Single(await SentToAsync(phone));
        Assert.Equal(await ExpectedAsync("en", day, "DRAFT - 3 months almsgiving"), sent.Body);
        Assert.Equal(MemorialReminders.DedupKey(plan, ThreeMonths, day), sent.DedupKey);
        Assert.Equal($"memorial:{plan}:{ThreeMonths}:{Iso(day)}", sent.DedupKey);
        Assert.Equal(await ExpectedAsync("si", day, "DRAFT - තෙමසේ දානය"), Assert.Single(await SentToAsync(sinhalaPhone)).Body);
        Assert.Empty(await SentToAsync(bookedPhone));
        Assert.Empty(await SentToAsync(archivedPhone));

        // The nights after, and on the day itself, nobody is texted twice; the day after, it is past.
        Assert.Equal(3, (await RemindersAt(day.AddDays(-6)).SendAsync()).Due);
        Assert.Equal(3, (await RemindersAt(day.AddDays(-1)).SendAsync()).Due);
        Assert.Equal(0, (await RemindersAt(day).SendAsync()).Due);
        Assert.Single(await SentToAsync(phone));
        Assert.Single(await SentToAsync(sinhalaPhone));
    }

    [Fact]
    public async Task TheHostRunsTheReminderJobAsThePlatform()
    {
        var (donor, phone) = await DonorAsync();
        var passing = Today.AddDays(-3);
        var (_, plan) = await PlanAsync(donor, passing);
        var day = await DayOfAsync(passing, SeventhDay);

        await _host.Services.GetRequiredService<MemorialReminderJob>().RunAsync(CancellationToken.None);

        Assert.Equal(MemorialReminders.DedupKey(plan, SeventhDay, day), Assert.Single(await SentToAsync(phone)).DedupKey);
    }

    // ------------------------------------------------------------------ archiving and who sees a plan

    [Fact]
    public async Task OnlyTheDonorArchivesTheirPlan_AndAnArchivedPlanIsGone()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (_, plan) = await PlanAsync(donor, Today.AddDays(-3));

        using (var response = await SendAsync(ClientFor(other), HttpMethod.Get, $"/api/memorial/plans/{plan}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(MemorialPlanResult.NotFound, (await BodyOf(response)).GetProperty("code").GetString());
        }

        using (var response = await SendAsync(ClientFor(other), HttpMethod.Post, $"/api/memorial/plans/{plan}/archive"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor, database.AlphaInstitutionId), HttpMethod.Post, $"/api/memorial/plans/{plan}/archive"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Row-Level Security aside (the platform sees every plan), only the donor's own id archives it.
        await using (var platform = ContextFor(TestCurrentUserContext.Platform()))
        {
            var handler = new MemorialPlanHandler(platform, new MemorialQueries(ConnectionsFor(TestCurrentUserContext.Platform())), TimeProvider.System);
            Assert.Equal(MemorialPlanResult.NotFound, await handler.ArchiveAsync(plan, other));
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/memorial/plans/{plan}/archive"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/memorial/plans/{plan}/archive"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, $"/api/memorial/plans/{plan}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/memorial/plans"))
        {
            Assert.Empty((await BodyOf(response)).EnumerateArray());
        }
    }

    [Fact]
    public async Task RowLevelSecurityShowsAPlanToItsDonorOnly_NeverToAnInstitution()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var (planId, plan) = await PlanAsync(donor, Today.AddDays(-3));

        async Task<int> VisibleTo(string key, object value)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = @key, @value = @value, @read_only = 1;", new { key, value });
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.MemorialPlans WHERE Id = @planId;", new { planId });
        }

        Assert.Equal(
            (1, 0, 0),
            (await VisibleTo("UserId", donor), await VisibleTo("UserId", other), await VisibleTo("InstitutionId", database.AlphaInstitutionId)));

        // The planner's own read, as the other donor, finds nothing even when asked for it by id; as
        // the platform, which sees every plan, it still reads only the donor it is asked about.
        Assert.Empty((await new MemorialQueries(ConnectionsFor(TestCurrentUserContext.ForUser(other))).LoadAsync(donor, plan)).Plans);
        var (_, othersPlan) = await PlanAsync(other, Today.AddDays(-3));
        Assert.Equal([plan], (await new MemorialQueries(ConnectionsFor(TestCurrentUserContext.Platform())).LoadAsync(donor, null)).Plans.Select(p => p.PublicId));
        Assert.Equal([othersPlan], (await new MemorialQueries(ConnectionsFor(TestCurrentUserContext.Platform())).LoadAsync(other, null)).Plans.Select(p => p.PublicId));

        // Nor can another donor write a plan in someone else's name, or a donor give theirs away.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'UserId', @value = @other, @read_only = 1;", new { other });
        var refused = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT booking.MemorialPlans (PublicId, DonorUserId, DeceasedName, DateOfPassing, ReligionLookupId)
            SELECT NEWID(), @donor, N'Someone', '2026-01-01', v.Id
            FROM core.LookupValues v JOIN core.LookupTypes t ON t.Id = v.LookupTypeId
            WHERE t.Code = 'RELIGION' AND v.Code = 'BUDDHISM';
            """,
            new { donor }));
        Assert.Contains("block predicate", refused.Message, StringComparison.OrdinalIgnoreCase);

        var givenAway = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE booking.MemorialPlans SET DonorUserId = @donor WHERE PublicId = @othersPlan;", new { donor, othersPlan }));
        Assert.Contains("block predicate", givenAway.Message, StringComparison.OrdinalIgnoreCase);
    }
}

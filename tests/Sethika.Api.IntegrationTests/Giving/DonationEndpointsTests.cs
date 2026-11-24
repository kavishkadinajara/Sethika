using System.Collections.Concurrent;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Giving.Donations;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Giving;

/// <summary>
/// Plan 09, D.1 over HTTP, against real SQL Server and Redis: recording a gift and its receipt number
/// (M1-M3), the receipt for its institution and its donor (M4), the receipt text (M5), the void (M6), the
/// trigger and RLS that keep recorded money unchanged (M7), and linking a donor's gifts (M3, K36, K37).
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them, with BOOKINGS
/// and GIVING on: the owner holds every GIVING.DONATIONS permission, staff VIEW and CREATE (M19).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class DonationEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "donation-test-signing-key-at-least-32-bytes!";
    private const string Donations = "/api/institution/donations";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    private Guid _alphaOwner;
    private Guid _alphaManager;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    private static string FirstReceipt => $"RCP-{Today.Year}-000001";

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

            -- GIVING is core (M19), and BOOKINGS for the donor book; the seed turns on BOOKINGS for Alpha only.
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
            builder.ConfigureTestServices(services => services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms)));
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
            new { users = _users, phones = _phones });
        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((phoneNumber, message));
            return Task.CompletedTask;
        }
    }

    private async Task<SqlConnection> ConnectionAsync(string stamp)
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(stamp);
        return connection;
    }

    private Task<SqlConnection> PlatformConnectionAsync() =>
        ConnectionAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");

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

    private string RandomPhone()
    {
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private async Task<Guid> UserAsync(string userType, string? phone = null, string language = "en")
    {
        var id = Guid.CreateVersion7();
        phone ??= RandomPhone();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, N'Giving Test', @phone, 1, @language, 'Active', 'stamp');
            """,
            new { id, userType, phone, language });

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

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<(Guid Id, string Phone)> DonorAsync(string language = "en")
    {
        var phone = RandomPhone();
        var id = await UserAsync("Donor", phone, language);
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;",
            new { id });
        return (id, phone);
    }

    private (HttpClient Client, string TokenId) ClientFor(Guid userId, int? institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return (client, tokens.Access.JwtId);
    }

    private HttpClient Alpha(Guid member) => ClientFor(member, database.AlphaInstitutionId).Client;

    private HttpClient Beta(Guid member) => ClientFor(member, database.BetaInstitutionId).Client;

    private HttpClient Personal(Guid user) => ClientFor(user, null).Client;

    /// <summary>An Alpha client whose token holds a step-up (plan A5).</summary>
    private async Task<HttpClient> SteppedUpAlphaAsync(Guid member)
    {
        var (client, tokenId) = ClientFor(member, database.AlphaInstitutionId);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(member, tokenId), 1, TimeSpan.FromMinutes(15));
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

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonElement.Parse(text).Clone();
    }

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

    private static Dictionary<string, object?> Cash(decimal amount = 5000m, string? name = "Walk-in Donor", Guid? donor = null, DateOnly? date = null, string method = "CASH") =>
        new()
        {
            ["kind"] = "Money",
            ["amount"] = amount,
            ["method"] = method,
            ["date"] = Iso(date ?? Today),
            ["donor"] = donor,
            ["donorName"] = donor is null ? name : null,
        };

    private static Dictionary<string, object?> Items(string items = "Fifty exercise books", decimal? quantity = 50m, Guid? donor = null) =>
        new()
        {
            ["kind"] = "InKind",
            ["items"] = items,
            ["quantity"] = quantity,
            ["date"] = Iso(Today),
            ["donor"] = donor,
            ["donorName"] = donor is null ? "Walk-in Donor" : null,
        };

    private sealed record Recorded(Guid PublicId, string ReceiptNo, string Version);

    private static async Task<Recorded> RecordAsync(HttpClient client, object body)
    {
        var response = await SendAsync(client, HttpMethod.Post, Donations, body);
        var json = await BodyOf(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, json.ToString());
        return new Recorded(json.GetProperty("publicId").GetGuid(), json.GetProperty("receiptNo").GetString()!, json.GetProperty("version").GetString()!);
    }

    /// <summary>A donor book row added by Alpha's owner: its id.</summary>
    private async Task<Guid> DonorBookAsync(string phone, bool consent, string language = "si")
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/donors",
            new { fullName = "Sunil Perera", phone, notes = (string?)null, language, textConsent = consent });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyOf(response)).GetProperty("publicId").GetGuid();
    }

    private static object Void(string version, string? reason = "Recorded twice by mistake.") => new { reason, version };

    /// <summary>Runs every waiting receipt text through the real handler, templates and the recording sender.</summary>
    private async Task SendTextsAsync()
    {
        var connections = new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform());
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);
        var processor = new OutboxProcessor(
            connections,
            [new DonationSmsHandler(new GivingQueries(connections), texts)],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

        await processor.RunAsync();
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'DonationRecorded' AND ProcessedAt IS NULL;", new { }));
    }

    private async Task<List<string>> TextsToAsync(string phone)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<string>("SELECT Body FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;", new { phone })];
    }

    // ------------------------------------------------------------------ recording and receipt numbers (M1, M2)

    [Fact]
    public async Task StaffRecordCash_AndEachInstitutionsReceiptsStartAtOne()
    {
        var alpha = await RecordAsync(Alpha(_alphaStaff), Cash());
        var beta = await RecordAsync(Beta(_betaOwner), Cash());
        var alphaNext = await RecordAsync(Alpha(_alphaStaff), Items());

        Assert.Equal(FirstReceipt, alpha.ReceiptNo);
        Assert.Equal(FirstReceipt, beta.ReceiptNo); // a series per institution: the same number in another book
        Assert.Equal($"RCP-{Today.Year}-000002", alphaNext.ReceiptNo);

        var stored = await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM giving.Donations d
            JOIN core.LookupValues m ON m.Id = d.PaymentMethodLookupId AND m.Code = 'CASH'
            WHERE d.PublicId = @id AND d.Kind = 'Money' AND d.Amount = 5000 AND d.DonorName = N'Walk-in Donor'
              AND d.RecordedBy = @staff AND d.InstitutionDonorId IS NULL AND d.DonorUserId IS NULL AND d.IsVoided = 0;
            """,
            new { id = alpha.PublicId, staff = _alphaStaff });
        Assert.Equal(1, stored);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditLogs WHERE EntityName = 'Donation' AND EntityId = @no AND Action = 'Record' AND InstitutionId = @alpha AND UserId = @staff;",
            new { no = alpha.ReceiptNo, alpha = database.AlphaInstitutionId, staff = _alphaStaff }));
    }

    [Fact]
    public async Task TwoRecordersAtOnce_GetConsecutiveNumbers()
    {
        var first = RecordAsync(Alpha(_alphaStaff), Cash(100));
        var second = RecordAsync(Alpha(_alphaOwner), Cash(200));
        var numbers = (await Task.WhenAll(first, second)).Select(r => r.ReceiptNo).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal([FirstReceipt, $"RCP-{Today.Year}-000002"], numbers);
    }

    [Fact]
    public async Task AGiftOnAPastDay_IsStoredAtNoonOnThatDayInColombo()
    {
        var day = Today.AddDays(-10);
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(date: day));

        var donatedAt = await ScalarAsync<DateTime>("SELECT DonatedAt FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId });
        Assert.Equal(SriLankaTime.ToUtc(day, new TimeOnly(12, 0)), donatedAt);
    }

    [Fact]
    public async Task AFormWithMistakes_SaysWhichFields()
    {
        var money = Cash();
        money["method"] = "PAYHERE"; // the gateway's own method, never chosen at the counter
        money["amount"] = 10.005m;
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, money), "amount", "method");

        var moneyWithItems = Cash();
        moneyWithItems["items"] = "Rice";
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, moneyWithItems), "items");

        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, Cash(date: Today.AddDays(1))), "date");
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, Cash(date: Today.AddYears(-1).AddDays(-1))), "date");
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, Cash(name: " ")), "donorName");

        var both = Cash(donor: Guid.NewGuid());
        both["donorName"] = "Someone";
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, both), "donor");

        var itemsWithMoney = Items();
        itemsWithMoney["amount"] = 100m;
        itemsWithMoney["items"] = " ";
        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, itemsWithMoney), "items", "amount");

        await AssertFieldsAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donations, new { kind = "Gold", date = Iso(Today), donorName = "X" }), "kind");

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations;", new { }));
    }

    [Fact]
    public async Task AnotherInstitutionsDonorBookRow_IsNotFound()
    {
        var phone = RandomPhone();
        var alphaRow = await DonorBookAsync(phone, consent: false);

        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Post, Donations, Cash(donor: alphaRow)), HttpStatusCode.NotFound, "donor_not_found");
    }

    [Fact]
    public async Task OnlyGivingPermissions_ReachTheEndpoints()
    {
        var (donor, _) = await DonorAsync();

        // A donor's personal session holds no institution permission.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Personal(donor), HttpMethod.Get, Donations)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Personal(donor), HttpMethod.Post, Donations, Cash())).StatusCode);

        // Without the module, nobody in the institution holds them (effective = grants ∩ enabled modules).
        await ExecuteAsync(
            "UPDATE im SET IsEnabled = 0 FROM org.InstitutionModules im JOIN auth.Modules m ON m.Id = im.ModuleId WHERE m.Code = 'GIVING' AND im.InstitutionId = @alpha; UPDATE org.Institutions SET PermissionsVersion += 1 WHERE Id = @alpha;",
            new { alpha = database.AlphaInstitutionId });
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, Donations)).StatusCode);
    }

    // ------------------------------------------------------------------ the receipt (M3, M4)

    [Fact]
    public async Task ADonorBookGift_SnapshotsTheRow_AndItsLinkedDonorReadsTheReceipt()
    {
        var (donor, phone) = await DonorAsync();
        var row = await DonorBookAsync(phone, consent: false); // linked: the phone is the donor's confirmed one
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(7500m, donor: row));

        var mine = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, "/api/me/donations"));
        var item = Assert.Single(mine.EnumerateArray());
        Assert.Equal(recorded.ReceiptNo, item.GetProperty("receiptNo").GetString());
        Assert.Equal("alpha-temple", item.GetProperty("institutionSlug").GetString());
        Assert.Equal(7500m, item.GetProperty("amount").GetDecimal());

        var receipt = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, $"/api/me/donations/{recorded.PublicId}"));
        Assert.Equal("Sunil Perera", receipt.GetProperty("donorName").GetString());
        Assert.Equal("Alpha Temple", receipt.GetProperty("institution").GetProperty("name").GetString());
        Assert.Equal("CASH", receipt.GetProperty("method").GetString());
        Assert.Equal("Giving Test", receipt.GetProperty("recordedByName").GetString());

        // The institution reads it through its own session; another donor and another institution do not.
        var institutionView = await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Donations}/{recorded.PublicId}");
        Assert.Equal(HttpStatusCode.OK, institutionView.StatusCode);

        var (other, _) = await DonorAsync();
        await AssertRefusedAsync(await SendAsync(Personal(other), HttpMethod.Get, $"/api/me/donations/{recorded.PublicId}"), HttpStatusCode.NotFound, "donation_not_found");
        Assert.Empty((await BodyOf(await SendAsync(Personal(other), HttpMethod.Get, "/api/me/donations"))).EnumerateArray());
        await AssertRefusedAsync(await SendAsync(Beta(_betaOwner), HttpMethod.Get, $"{Donations}/{recorded.PublicId}"), HttpStatusCode.NotFound, "donation_not_found");
    }

    [Fact]
    public async Task TheList_HoldsTheMonthsGifts_MarksMine_AndVoidedOnesStay()
    {
        var mine = await RecordAsync(Alpha(_alphaStaff), Cash(100));
        var theirs = await RecordAsync(Alpha(_alphaOwner), Cash(200));
        await RecordAsync(Alpha(_alphaStaff), Cash(300, date: new DateOnly(Today.Year, Today.Month, 1).AddDays(-1))); // last month

        var voided = await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{mine.ReceiptNo}/void", Void(mine.Version));
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var list = (await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, Donations))).EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        var mineRow = list.Single(r => r.GetProperty("receiptNo").GetString() == mine.ReceiptNo);
        var theirsRow = list.Single(r => r.GetProperty("receiptNo").GetString() == theirs.ReceiptNo);
        Assert.True(mineRow.GetProperty("recordedByMe").GetBoolean());
        Assert.True(mineRow.GetProperty("isVoided").GetBoolean());
        Assert.False(theirsRow.GetProperty("recordedByMe").GetBoolean());

        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var earlier = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Donations}?month={lastMonth:yyyy-MM}"));
        Assert.Equal(300m, Assert.Single(earlier.EnumerateArray()).GetProperty("amount").GetDecimal());

        await AssertRefusedAsync(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Donations}?month=2026-13"), HttpStatusCode.BadRequest, "invalid_month");
    }

    [Fact]
    public async Task TheCounterMethods_AreListedInTheirOrder_WithoutTheGateway()
    {
        var methods = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Donations}/methods"));
        Assert.Equal(["CASH", "BANK_TRANSFER", "CHEQUE", "CARD"], methods.EnumerateArray().Select(m => m.GetProperty("code").GetString()!).ToArray());
        Assert.Equal("මුදල්", methods[0].GetProperty("nameSi").GetString());
    }

    // ------------------------------------------------------------------ the receipt text (M5)

    [Fact]
    public async Task TheReceiptText_GoesToTheLinkedAccount_OrToAConsentingRow_Once()
    {
        // The institution's name goes in the account's language, not the donor book row's (si by default).
        await ExecuteAsync("UPDATE org.Institutions SET NameSi = N'ඇල්ෆා විහාරය' WHERE Id = @alpha;", new { alpha = database.AlphaInstitutionId });
        var (donor, donorPhone) = await DonorAsync(language: "en");
        var linked = await DonorBookAsync(donorPhone, consent: false);
        var consentingPhone = RandomPhone();
        var consenting = await DonorBookAsync(consentingPhone, consent: true, language: "en");
        var silentPhone = RandomPhone();
        var silent = await DonorBookAsync(silentPhone, consent: false);
        var walkInPhone = RandomPhone();

        var toAccount = await RecordAsync(Alpha(_alphaStaff), Cash(5000m, donor: linked));
        var toRow = await RecordAsync(Alpha(_alphaStaff), Items(donor: consenting));
        await RecordAsync(Alpha(_alphaStaff), Cash(donor: silent));
        var walkIn = Cash();
        walkIn["donorPhone"] = walkInPhone;
        await RecordAsync(Alpha(_alphaStaff), walkIn);

        // A walk-in's typed phone is not consent: no message is even written for it.
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'DonationRecorded';", new { }));

        await SendTextsAsync();
        await ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = NULL, Attempts = 0 WHERE EventType = 'DonationRecorded';", new { });
        await SendTextsAsync(); // run again: the dedup key keeps it to one text each

        var accountTexts = await TextsToAsync(donorPhone);
        Assert.Equal([$"Sethika {toAccount.ReceiptNo}: Alpha Temple, LKR 5,000.00. Your gift was received. Thank you."], accountTexts);
        Assert.Equal([$"Sethika {toRow.ReceiptNo}: Alpha Temple. Your gift of items was received. Thank you."], await TextsToAsync(consentingPhone));
        Assert.Empty(await TextsToAsync(silentPhone));
        Assert.Empty(await TextsToAsync(walkInPhone));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM notify.Notifications WHERE DedupKey = @key;",
            new { key = DonationTexts.DedupKey(database.AlphaInstitutionId, toAccount.ReceiptNo) }));
    }

    [Fact]
    public async Task AGiftVoidedBeforeItsText_IsNotTexted()
    {
        var phone = RandomPhone();
        var row = await DonorBookAsync(phone, consent: true);
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(donor: row));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version))).StatusCode);

        await SendTextsAsync();
        Assert.Empty(await TextsToAsync(phone));
    }

    // ------------------------------------------------------------------ the void (M6)

    [Fact]
    public async Task ASecondPersonVoids_WithAStepUp_AndTheGiftStaysMarked()
    {
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash());

        // Staff may record but not void.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version))).StatusCode);
        // The owner may, but only stepped up.
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version)), HttpStatusCode.Forbidden, "stepup_required");
        // A void needs a reason.
        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version, reason: "  ")), "reason");

        var voided = await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version));
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var receipt = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, $"{Donations}/{recorded.PublicId}"));
        Assert.True(receipt.GetProperty("isVoided").GetBoolean());
        Assert.Equal("Recorded twice by mistake.", receipt.GetProperty("voidReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, receipt.GetProperty("voidedAt").ValueKind);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLogs WHERE EntityName = 'Donation' AND EntityId = @no AND Action = 'Void' AND UserId = @owner;",
            new { no = recorded.ReceiptNo, owner = _alphaOwner }));

        var newVersion = (await BodyOf(voided)).GetProperty("version").GetString()!;
        await AssertRefusedAsync(await SendAsync(await SteppedUpAlphaAsync(_alphaManager), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(newVersion)), HttpStatusCode.Conflict, "already_void");
    }

    [Fact]
    public async Task TheRecorderCannotVoidTheirOwnGift_EvenSteppedUp()
    {
        var recorded = await RecordAsync(Alpha(_alphaOwner), Cash());

        await AssertRefusedAsync(
            await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version)),
            HttpStatusCode.Forbidden,
            "void_own_record");
        Assert.False(await ScalarAsync<bool>("SELECT IsVoided FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));
    }

    [Fact]
    public async Task AVoidOnAStaleVersion_IsRefused_AndAnotherInstitutionsReceiptIsNotFound()
    {
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash());
        var stale = Convert.ToBase64String(new byte[8]);

        await AssertRefusedAsync(await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(stale)), HttpStatusCode.Conflict, "stale");

        // Beta's own first receipt has the same number; Beta's void reaches only Beta's.
        Assert.False(await ScalarAsync<bool>("SELECT IsVoided FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));
        var (betaClient, tokenId) = ClientFor(_betaOwner, database.BetaInstitutionId);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(_betaOwner, tokenId), 1, TimeSpan.FromMinutes(15));
        await AssertRefusedAsync(await SendAsync(betaClient, HttpMethod.Post, $"{Donations}/{recorded.ReceiptNo}/void", Void(recorded.Version)), HttpStatusCode.NotFound, "donation_not_found");
    }

    // ------------------------------------------------------------------ the database's own guarantees (M7)

    private async Task<string?> SqlErrorAsync(string stamp, string sql, object parameters)
    {
        await using var connection = await ConnectionAsync(stamp);
        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return null;
        }
        catch (SqlException ex)
        {
            return $"{ex.Number}: {ex.Message}";
        }
    }

    private const string Platform = "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;";

    [Theory]
    [InlineData("UPDATE giving.Donations SET Amount = 1 WHERE PublicId = @id;")]
    [InlineData("UPDATE giving.Donations SET DonorName = N'walk-in donor' WHERE PublicId = @id;")] // case only
    [InlineData("UPDATE giving.Donations SET DonatedAt = DATEADD(DAY, -1, DonatedAt) WHERE PublicId = @id;")]
    [InlineData("UPDATE giving.Donations SET Note = N'added later' WHERE PublicId = @id;")]
    [InlineData("UPDATE giving.Donations SET DonorUserId = @someone WHERE PublicId = @id;")] // a walk-in's never moves
    public async Task RawSql_CannotChangeRecordedMoney(string sql)
    {
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash());
        var error = await SqlErrorAsync(Platform, sql, new { id = recorded.PublicId, someone = _alphaOwner });

        Assert.NotNull(error);
        Assert.StartsWith("51101", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawSql_CannotDeleteAGift_UnvoidOne_OrVoidItAsTheRecorder()
    {
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash());

        Assert.StartsWith("51100", await SqlErrorAsync(Platform, "DELETE FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }), StringComparison.Ordinal);

        // The recorder voiding: CK_Donations_VoidFourEyes (547), whatever the API says.
        var own = await SqlErrorAsync(Platform,
            "UPDATE giving.Donations SET IsVoided = 1, VoidedBy = RecordedBy, VoidReason = N'x', VoidedAt = SYSUTCDATETIME() WHERE PublicId = @id;",
            new { id = recorded.PublicId });
        Assert.NotNull(own);
        Assert.Contains("CK_Donations_VoidFourEyes", own, StringComparison.Ordinal);

        Assert.Null(await SqlErrorAsync(Platform,
            "UPDATE giving.Donations SET IsVoided = 1, VoidedBy = @owner, VoidReason = N'x', VoidedAt = SYSUTCDATETIME() WHERE PublicId = @id;",
            new { id = recorded.PublicId, owner = _alphaOwner }));

        Assert.StartsWith("51101", await SqlErrorAsync(Platform,
            "UPDATE giving.Donations SET IsVoided = 0, VoidedBy = NULL, VoidReason = NULL, VoidedAt = NULL WHERE PublicId = @id;",
            new { id = recorded.PublicId }), StringComparison.Ordinal);
        Assert.StartsWith("51101", await SqlErrorAsync(Platform,
            "UPDATE giving.Donations SET VoidReason = N'another reason' WHERE PublicId = @id;",
            new { id = recorded.PublicId }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawSql_MayMoveOnlyADonorBookGiftsAccount()
    {
        var row = await DonorBookAsync(RandomPhone(), consent: false);
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(donor: row));
        var (donor, _) = await DonorAsync();

        Assert.Null(await SqlErrorAsync(Platform, "UPDATE giving.Donations SET DonorUserId = @donor WHERE PublicId = @id;", new { id = recorded.PublicId, donor }));
        Assert.Equal(donor, await ScalarAsync<Guid>("SELECT DonorUserId FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));
    }

    [Fact]
    public async Task ADonorsSession_ReadsItsGift_ButCannotWriteOrMakeOne()
    {
        var (donor, phone) = await DonorAsync();
        var row = await DonorBookAsync(phone, consent: false);
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(donor: row));
        var asDonor = $"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{donor}', @read_only = 1;";

        await using (var connection = await ConnectionAsync(asDonor))
        {
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations;"));
        }

        // A void from the donor's own session: the block after update (fn_Tenant), not the trigger.
        var update = await SqlErrorAsync(asDonor,
            "UPDATE giving.Donations SET IsVoided = 1, VoidedBy = @donor, VoidReason = N'x', VoidedAt = SYSUTCDATETIME() WHERE PublicId = @id;",
            new { id = recorded.PublicId, donor });
        Assert.NotNull(update);
        Assert.StartsWith("33504", update, StringComparison.Ordinal);

        // A receipt made in the donor's own session, for themselves: the block after insert (fn_Tenant).
        var insert = await SqlErrorAsync(asDonor,
            """
            INSERT giving.Donations (PublicId, ReceiptNo, InstitutionId, DonorUserId, DonorName, Kind, Amount, PaymentMethodLookupId, DonatedAt, RecordedBy)
            SELECT NEWID(), 'RCP-FORGED', @alpha, @donor, N'Me', 'Money', 1000000,
                   (SELECT v.Id FROM core.LookupValues v JOIN core.LookupTypes t ON t.Id = v.LookupTypeId WHERE t.Code = 'PAYMENT_METHOD' AND v.Code = 'CASH'),
                   SYSUTCDATETIME(), @donor;
            """,
            new { alpha = database.AlphaInstitutionId, donor });
        Assert.NotNull(insert);
        Assert.StartsWith("33504", insert, StringComparison.Ordinal);

        // Another institution's session sees none of Alpha's gifts.
        var asBeta = $"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.BetaInstitutionId}, @read_only = 1;";
        await using var beta = await ConnectionAsync(asBeta);
        Assert.Equal(0, await beta.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations;"));
    }

    // ------------------------------------------------------------------ linking (M3, K36, K37)

    [Fact]
    public async Task LinkingAtSignIn_CarriesTheRowsGifts_VoidedOnesToo()
    {
        var phone = RandomPhone();
        var row = await DonorBookAsync(phone, consent: false); // no account yet: nothing to link
        var kept = await RecordAsync(Alpha(_alphaStaff), Cash(donor: row));
        var voided = await RecordAsync(Alpha(_alphaStaff), Items(donor: row));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(await SteppedUpAlphaAsync(_alphaOwner), HttpMethod.Post, $"{Donations}/{voided.ReceiptNo}/void", Void(voided.Version))).StatusCode);

        var donor = await UserAsync("Donor", phone);
        Assert.Equal(1, await new SqlDonorBookLinks(database.ConnectionString).LinkAsync(donor));

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations WHERE DonorUserId = @donor AND PublicId IN @ids;",
            new { donor, ids = new[] { kept.PublicId, voided.PublicId } }));
    }

    [Fact]
    public async Task ChangingARowsPhoneToAnotherAccount_MovesItsGifts()
    {
        var (first, firstPhone) = await DonorAsync();
        var (second, secondPhone) = await DonorAsync();
        var row = await DonorBookAsync(firstPhone, consent: false);
        var recorded = await RecordAsync(Alpha(_alphaStaff), Cash(donor: row));
        Assert.Equal(first, await ScalarAsync<Guid>("SELECT DonorUserId FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));

        var book = await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/donors"));
        var version = book.EnumerateArray().Single(d => d.GetProperty("publicId").GetGuid() == row).GetProperty("version").GetString();
        var changed = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"/api/institution/donors/{row}",
            new { fullName = "Sunil Perera", phone = secondPhone, language = "si", textConsent = false, version });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        Assert.Equal(second, await ScalarAsync<Guid>("SELECT DonorUserId FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));
        // The name on the receipt is still the one given at the time.
        Assert.Equal("Sunil Perera", await ScalarAsync<string>("SELECT DonorName FROM giving.Donations WHERE PublicId = @id;", new { id = recorded.PublicId }));
    }
}

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
/// Plan 09, D.3 over HTTP, against real SQL Server and Redis: each institution's merchant and its sealed
/// secret (M10, M21), the order and the checkout form (M8), and the signed notification that alone makes a
/// donation (M9), with the rules decided while building (M32-M36). No test calls PayHere: the
/// notifications are signed here with test secrets, by a formula written out in this class, not
/// <c>PayHereSignature</c>, so a change to that class cannot change what the tests expect.
/// </summary>
/// <remarks>Alpha's and Beta's members hold the template roles: only the owner holds GIVING.PAYMENTS.</remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class PaymentEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "payment-test-signing-key-at-least-32-bytes-long!";
    private const string Merchant = "/api/institution/payments/merchant";
    private const string Notify = "/api/payments/payhere/notify";
    private const string Platform = "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;";

    private const string AlphaMerchant = "1211149";
    private const string AlphaSecret = "alpha-merchant-secret-Mzk1";
    private const string BetaMerchant = "1211150";
    private const string BetaSecret = "beta-merchant-secret-Njc4";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    private Guid _alphaOwner;
    private Guid _alphaManager;
    private Guid _alphaStaff;
    private Guid _betaOwner;
    private string _alphaSlug = null!;
    private string _betaSlug = null!;

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

            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT i.Id, m.Id, 1, 'Manual', @changedBy
            FROM auth.Modules m CROSS JOIN org.Institutions i
            WHERE i.Id IN (@alpha, @beta) AND m.Code = 'GIVING';

            UPDATE org.Institutions SET Status = 'Active', IsPubliclyListed = 1 WHERE Id IN (@alpha, @beta);
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });

        _alphaOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");
        _alphaManager = await MemberAsync(database.AlphaInstitutionId, "MANAGER");
        _alphaStaff = await MemberAsync(database.AlphaInstitutionId, "STAFF");
        _betaOwner = await MemberAsync(database.BetaInstitutionId, "OWNER");
        _alphaSlug = await ScalarAsync<string>("SELECT Slug FROM org.Institutions WHERE Id = @id;", new { id = database.AlphaInstitutionId });
        _betaSlug = await ScalarAsync<string>("SELECT Slug FROM org.Institutions WHERE Id = @id;", new { id = database.BetaInstitutionId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);

            // The addresses PayHere is sent to, pinned as the rest are: a developer's user-secrets
            // point them at a tunnel for a sandbox walk, and must not change what these tests see.
            builder.UseSetting("Payments:PayHere:WebPublicUrl", "http://localhost:3000");
            builder.UseSetting("Payments:PayHere:ApiPublicUrl", "http://localhost:3000");
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

    private async Task<SqlConnection> ConnectionAsync(string stamp)
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        if (stamp.Length > 0)
        {
            await connection.ExecuteAsync(stamp);
        }

        return connection;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await ConnectionAsync(Platform);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await ConnectionAsync(Platform);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private async Task<Guid> UserAsync(string userType, string name = "Payment Test", bool confirmed = true, string language = "en")
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, @userType, @name, @phone, @confirmed, @language, 'Active', 'stamp');
            """,
            new { id, userType, name, phone, confirmed, language });

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

    private async Task<Guid> DonorAsync(string name = "Nimal Perera", bool confirmed = true, string language = "en")
    {
        var id = await UserAsync("Donor", name, confirmed, language);
        await ExecuteAsync(
            "INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive) SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;",
            new { id });
        return id;
    }

    private (HttpClient Client, string TokenId) ClientFor(Guid userId, int? institutionId)
    {
        var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return (client, tokens.Access.JwtId);
    }

    private HttpClient Alpha(Guid member) => ClientFor(member, database.AlphaInstitutionId).Client;

    private HttpClient Personal(Guid user) => ClientFor(user, null).Client;

    private HttpClient Anonymous() => _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<HttpClient> SteppedUpAsync(Guid member, int institutionId)
    {
        var (client, tokenId) = ClientFor(member, institutionId);
        await _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(member, tokenId), 1, TimeSpan.FromMinutes(15));
        return client;
    }

    private Task<HttpClient> SteppedUpAlphaAsync() => SteppedUpAsync(_alphaOwner, database.AlphaInstitutionId);

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
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        var body = await BodyOf(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(fields, body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    private static async Task AssertOkAsync(HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    // ------------------------------------------------------------------ PayHere's side, written out

#pragma warning disable CA5351 // PayHere's formula.
    private static string Md5(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));
#pragma warning restore CA5351

    private static string CheckoutHash(string merchantId, string orderId, string amount, string secret) =>
        Md5(merchantId + orderId + amount + "LKR" + Md5(secret));

    private static string Md5Sig(string merchantId, string orderId, string amount, string currency, string status, string secret) =>
        Md5(merchantId + orderId + amount + currency + status + Md5(secret));

    /// <summary>What PayHere posts: form-encoded, signed with <paramref name="secret"/>.</summary>
    private async Task<HttpResponseMessage> NotifyAsync(
        Guid order,
        string amount = "2500.00",
        string status = "2",
        string merchant = AlphaMerchant,
        string secret = AlphaSecret,
        string currency = "LKR",
        string? signature = null,
        string paymentId = "320025012345")
    {
        using var client = Anonymous();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_id"] = merchant,
            ["order_id"] = order.ToString(),
            ["payment_id"] = paymentId,
            ["payhere_amount"] = amount,
            ["payhere_currency"] = currency,
            ["status_code"] = status,
            ["md5sig"] = signature ?? Md5Sig(merchant, order.ToString(), amount, currency, status, secret),
            ["method"] = "VISA",
            ["status_message"] = "Successfully received the VISA payment",
        });

        return await client.PostAsync(Notify, form);
    }

    // ------------------------------------------------------------------ helpers

    private static Dictionary<string, object?> MerchantBody(string? merchantId = AlphaMerchant, string? secret = AlphaSecret, bool enabled = true, string? version = null) =>
        new() { ["merchantId"] = merchantId, ["secret"] = secret, ["enabled"] = enabled, ["version"] = version };

    private async Task<string> SetAlphaMerchantAsync(bool enabled = true)
    {
        var response = await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(enabled: enabled));
        await AssertOkAsync(response);
        return (await BodyOf(response)).GetProperty("version").GetString()!;
    }

    private async Task SetBetaMerchantAsync()
    {
        var client = await SteppedUpAsync(_betaOwner, database.BetaInstitutionId);
        await AssertOkAsync(await SendAsync(client, HttpMethod.Put, Merchant, MerchantBody(BetaMerchant, BetaSecret)));
    }

    private static Dictionary<string, object?> Gift(decimal? amount = 2500m, Guid? need = null, string? email = "nimal@example.com", string? address = "12 Temple Road", string? city = "Kandy") =>
        new() { ["amount"] = amount, ["need"] = need, ["email"] = email, ["address"] = address, ["city"] = city };

    private Task<HttpResponseMessage> TryGiveAsync(Guid donor, string slug, Dictionary<string, object?>? gift = null) =>
        SendAsync(Personal(donor), HttpMethod.Post, $"/api/institutions/{slug}/give", gift ?? Gift());

    /// <summary>An order of <paramref name="donor"/>'s to Alpha.</summary>
    private async Task<Guid> OrderAsync(Guid donor, Dictionary<string, object?>? gift = null)
    {
        var response = await TryGiveAsync(donor, _alphaSlug, gift);
        await AssertOkAsync(response);
        return (await BodyOf(response)).GetProperty("orderId").GetGuid();
    }

    private sealed record OrderRow(string Status, long? DonationId, short? Code, string? PaymentId, DateTime? NotifiedAt);

    private async Task<OrderRow> OrderRowAsync(Guid order)
    {
        await using var connection = await ConnectionAsync(Platform);
        return await connection.QuerySingleAsync<OrderRow>(
            "SELECT Status AS Status, DonationId AS DonationId, PayHereStatusCode AS Code, PayHerePaymentId AS PaymentId, NotifiedAt AS NotifiedAt FROM giving.OnlinePayments WHERE OrderId = @order;",
            new { order });
    }

    private Task<int> DonationCountAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM giving.Donations;", new { });

    private Task<string> ReceiptOfAsync(Guid order) =>
        ScalarAsync<string>(
            "SELECT d.ReceiptNo FROM giving.Donations d JOIN giving.OnlinePayments p ON p.DonationId = d.Id WHERE p.OrderId = @order;",
            new { order });

    /// <summary>An open need of Alpha's.</summary>
    private async Task<Guid> OpenNeedAsync(DateOnly? deadline = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Exercise books",
            ["description"] = null,
            ["category"] = "STATIONERY",
            ["unit"] = "PIECES",
            ["quantity"] = 50m,
            ["priority"] = "Normal",
            ["deadline"] = deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        var created = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, "/api/institution/needs", body);
        await AssertOkAsync(created);
        var need = (await BodyOf(created)).GetProperty("publicId").GetGuid();
        var version = Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM giving.Needs WHERE PublicId = @need;", new { need }));
        await AssertOkAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, $"/api/institution/needs/{need}/open", new { version }));
        return need;
    }

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
    }

    private async Task<List<string>> TextsToAsync(Guid user)
    {
        await using var connection = await ConnectionAsync(Platform);
        return [.. await connection.QueryAsync<string>(
            "SELECT n.Body FROM notify.Notifications n JOIN auth.Users u ON u.PhoneNumber = n.Recipient WHERE u.Id = @user ORDER BY n.Id;",
            new { user })];
    }

    // ------------------------------------------------------------------ the merchant (M10)

    [Fact]
    public async Task SavingTheMerchant_NeedsAStepUp()
    {
        await AssertRefusedAsync(await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, Merchant, MerchantBody()), HttpStatusCode.Forbidden, "stepup_required");

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.PaymentMerchants;", new { }));
    }

    [Fact]
    public async Task OnlyTheOwnerHoldsTheMerchantScreen()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, Merchant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaManager), HttpMethod.Get, Merchant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, Merchant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaManager), HttpMethod.Put, Merchant, MerchantBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Personal(await DonorAsync()), HttpMethod.Get, Merchant)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Get, Merchant)).StatusCode);
    }

    [Fact]
    public async Task TheSecret_IsStoredSealed_AndTheApiNeverReturnsIt()
    {
        await SetAlphaMerchantAsync();

        var text = await (await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, Merchant)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(AlphaSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("cipher", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);

        var merchant = JsonDocument.Parse(text).RootElement.GetProperty("merchant");
        Assert.Equal(AlphaMerchant, merchant.GetProperty("merchantId").GetString());
        Assert.True(merchant.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("Payment Test", merchant.GetProperty("updatedByName").GetString());

        var cipher = await ScalarAsync<byte[]>("SELECT SecretCipher FROM giving.PaymentMerchants;", new { });
        Assert.Equal(AlphaSecret.Length + 16, cipher.Length);
        Assert.DoesNotContain(AlphaSecret, Encoding.UTF8.GetString(cipher), StringComparison.Ordinal);
        Assert.Equal("test1", await ScalarAsync<string>("SELECT SecretKeyId FROM giving.PaymentMerchants;", new { }));

        // Audit rows outlive a run, so this run's is the newest.
        var audit = await ScalarAsync<string>("SELECT TOP (1) NewValues FROM audit.AuditLogs WHERE EntityName = 'PaymentMerchant' AND EntityId = @id ORDER BY Id DESC;", new { id = AlphaMerchant });
        Assert.Contains("\"secretChanged\":true", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(AlphaSecret, audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMerchantYet_ReadsAsNone() =>
        Assert.Equal(JsonValueKind.Null, (await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, Merchant))).GetProperty("merchant").ValueKind);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("1234567890123456789012345678901")]
    public async Task AMerchantIdMustBeLettersAndDigits(string? merchantId) =>
        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(merchantId: merchantId)), "merchantId");

    [Fact]
    public async Task TheFirstSave_NeedsASecret() =>
        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: "   ")), "secret");

    [Fact]
    public async Task ASecretTooLongToStore_IsRefused() =>
        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: new string('x', 201))), "secret");

    [Fact]
    public async Task ASaveWithoutASecret_KeepsTheStoredOne()
    {
        var version = await SetAlphaMerchantAsync();

        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: null, enabled: true, version: version)));

        var form = (await BodyOf(await TryGiveAsync(await DonorAsync(), _alphaSlug))).GetProperty("fields");
        Assert.Equal(CheckoutHash(AlphaMerchant, form.GetProperty("order_id").GetString()!, "2500.00", AlphaSecret), form.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task ANewSecret_ReplacesTheOldOne()
    {
        var version = await SetAlphaMerchantAsync();

        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: "a-new-secret", version: version)));

        var form = (await BodyOf(await TryGiveAsync(await DonorAsync(), _alphaSlug))).GetProperty("fields");
        Assert.Equal(CheckoutHash(AlphaMerchant, form.GetProperty("order_id").GetString()!, "2500.00", "a-new-secret"), form.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task ChangingTheMerchantId_AsksForItsSecret()
    {
        var version = await SetAlphaMerchantAsync();

        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody("9999999", null, version: version)), "secret");
    }

    [Fact]
    public async Task ASaveFromAnOldRead_IsStale()
    {
        var version = await SetAlphaMerchantAsync();
        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(version: version)));

        await AssertRefusedAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(version: version)), HttpStatusCode.Conflict, "stale");
    }

    [Fact]
    public async Task ASaveOverAMerchant_MustSayWhichVersionItRead()
    {
        await SetAlphaMerchantAsync();

        await AssertFieldsAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody()), "version");
    }

    [Fact]
    public async Task EachInstitution_SeesOnlyItsOwnMerchant()
    {
        await SetAlphaMerchantAsync();
        await SetBetaMerchantAsync();

        var beta = (await BodyOf(await SendAsync(ClientFor(_betaOwner, database.BetaInstitutionId).Client, HttpMethod.Get, Merchant))).GetProperty("merchant");
        Assert.Equal(BetaMerchant, beta.GetProperty("merchantId").GetString());

        async Task<int> SeenBy(string stamp)
        {
            await using var connection = await ConnectionAsync(stamp);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM giving.PaymentMerchants;");
        }

        Assert.Equal(1, await SeenBy($"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.AlphaInstitutionId}, @read_only = 1;"));
        Assert.Equal(0, await SeenBy($"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{await DonorAsync()}', @read_only = 1;"));
        Assert.Equal(0, await SeenBy(string.Empty));
    }

    [Fact]
    public async Task ThePublicPage_SaysOnlyWhetherGivingIsOn()
    {
        async Task<string> Page() => await (await SendAsync(Anonymous(), HttpMethod.Get, $"/api/institutions/{_alphaSlug}")).Content.ReadAsStringAsync();

        // Another institution's merchant says nothing about this one.
        await SetBetaMerchantAsync();
        Assert.False(JsonDocument.Parse(await Page()).RootElement.GetProperty("givesOnline").GetBoolean());

        var version = await SetAlphaMerchantAsync();
        var on = await Page();
        Assert.True(JsonDocument.Parse(on).RootElement.GetProperty("givesOnline").GetBoolean());
        Assert.DoesNotContain(AlphaMerchant, on, StringComparison.Ordinal);

        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: null, enabled: false, version: version)));
        Assert.False(JsonDocument.Parse(await Page()).RootElement.GetProperty("givesOnline").GetBoolean());
    }

    // ------------------------------------------------------------------ the order (M8)

    [Fact]
    public async Task AnOrder_AnswersPayHeresForm_AndIsNotADonation()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync("Nimal Kumara Perera", language: "si");

        var response = await TryGiveAsync(donor, _alphaSlug, Gift(2500.5m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        var order = body.GetProperty("orderId").GetGuid();
        var form = body.GetProperty("fields");
        string Field(string name) => form.GetProperty(name).GetString()!;

        Assert.Equal("https://sandbox.payhere.lk/pay/checkout", body.GetProperty("action").GetString());
        Assert.Equal(AlphaMerchant, Field("merchant_id"));
        Assert.Equal(order.ToString(), Field("order_id"));
        Assert.Equal("2500.50", Field("amount"));
        Assert.Equal("LKR", Field("currency"));
        Assert.Equal(CheckoutHash(AlphaMerchant, order.ToString(), "2500.50", AlphaSecret), Field("hash"));
        Assert.Equal("Nimal", Field("first_name"));
        Assert.Equal("Kumara Perera", Field("last_name"));
        Assert.Equal("nimal@example.com", Field("email"));
        Assert.Equal("12 Temple Road", Field("address"));
        Assert.Equal("Kandy", Field("city"));
        Assert.Equal("Sri Lanka", Field("country"));
        Assert.Equal(await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @donor;", new { donor }), Field("phone"));
        Assert.Equal($"http://localhost:3000/si/giving/return/{order}", Field("return_url"));
        Assert.Equal($"http://localhost:3000/si/giving/return/{order}?left=1", Field("cancel_url"));
        Assert.Equal("http://localhost:3000/api/payments/payhere/notify", Field("notify_url"));
        Assert.False(string.IsNullOrWhiteSpace(Field("items")));

        var row = await OrderRowAsync(order);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.DonationId);
        Assert.Equal(0, await DonationCountAsync());
        Assert.Equal(donor, await ScalarAsync<Guid>("SELECT DonorUserId FROM giving.OnlinePayments WHERE OrderId = @order;", new { order }));
        Assert.Equal(AlphaMerchant, await ScalarAsync<string>("SELECT MerchantId FROM giving.OnlinePayments WHERE OrderId = @order;", new { order }));
    }

    [Fact]
    public async Task APersonWithOneName_GivesItForBoth()
    {
        await SetAlphaMerchantAsync();

        var form = (await BodyOf(await TryGiveAsync(await DonorAsync("Sumana"), _alphaSlug))).GetProperty("fields");

        Assert.Equal("Sumana", form.GetProperty("first_name").GetString());
        Assert.Equal("Sumana", form.GetProperty("last_name").GetString());
    }

    [Fact]
    public async Task TheEmailAndAddress_AreNotStored()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();

        await OrderAsync(donor, Gift(email: "only-for-payhere@example.com", address: "Only for PayHere"));

        Assert.Null(await ScalarAsync<string?>("SELECT Email FROM auth.Users WHERE Id = @donor;", new { donor }));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditLogs WHERE NewValues LIKE '%only-for-payhere%' OR NewValues LIKE '%Only for PayHere%';", new { }));
    }

    [Theory]
    [InlineData("99.99")]
    [InlineData("1000000.01")]
    [InlineData("100.001")]
    [InlineData("0")]
    public async Task AnAmountOutsideTheLimits_IsRefused(string amount)
    {
        await SetAlphaMerchantAsync();

        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(decimal.Parse(amount, CultureInfo.InvariantCulture))), "amount");
    }

    [Theory]
    [InlineData("100")]
    [InlineData("1000000")]
    public async Task TheLimitsThemselves_AreAllowed(string amount)
    {
        await SetAlphaMerchantAsync();

        await AssertOkAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(decimal.Parse(amount, CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task WhatPayHereRequires_MustBeThere()
    {
        await SetAlphaMerchantAsync();

        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(null, null, null, " ", "")), "amount", "email", "address", "city");
        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(email: "not an email")), "email");
        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(email: "Nimal <nimal@example.com>")), "email");
        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(address: new string('x', 201))), "address");
        await AssertFieldsAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug, Gift(city: new string('x', 101))), "city");
    }

    [Fact]
    public async Task WithoutAnEnabledMerchant_OnlineGivingIsOff()
    {
        await AssertRefusedAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug), HttpStatusCode.Conflict, "online_giving_off");

        await SetAlphaMerchantAsync(enabled: false);
        await AssertRefusedAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug), HttpStatusCode.Conflict, "online_giving_off");
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.OnlinePayments;", new { }));
    }

    [Fact]
    public async Task AnInstitutionNoLongerActive_TakesNoGifts()
    {
        await SetAlphaMerchantAsync();
        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Id = @id;", new { id = database.AlphaInstitutionId });

        await AssertRefusedAsync(await TryGiveAsync(await DonorAsync(), _alphaSlug), HttpStatusCode.NotFound, "institution_not_found");
        await AssertRefusedAsync(await TryGiveAsync(await DonorAsync(), "no-such-place"), HttpStatusCode.NotFound, "institution_not_found");
    }

    [Fact]
    public async Task GivingNeedsAConfirmedPhone_AndADonorsSession()
    {
        await SetAlphaMerchantAsync();

        await AssertRefusedAsync(await TryGiveAsync(await DonorAsync(confirmed: false), _alphaSlug), HttpStatusCode.Forbidden, "phone_required");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Post, $"/api/institutions/{_alphaSlug}/give", Gift())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, $"/api/institutions/{_alphaSlug}/give", Gift())).StatusCode);
    }

    [Fact]
    public async Task AGiftForANeed_MustNameAnOpenNeedOfThatInstitution()
    {
        await SetAlphaMerchantAsync();
        await SetBetaMerchantAsync();
        var need = await OpenNeedAsync();
        var donor = await DonorAsync();

        await AssertRefusedAsync(await TryGiveAsync(donor, _betaSlug, Gift(need: need)), HttpStatusCode.NotFound, "need_not_found");
        await AssertRefusedAsync(await TryGiveAsync(donor, _alphaSlug, Gift(need: Guid.CreateVersion7())), HttpStatusCode.NotFound, "need_not_found");

        await ExecuteAsync("UPDATE giving.Needs SET Status = 'Draft' WHERE PublicId = @need;", new { need });
        await AssertRefusedAsync(await TryGiveAsync(donor, _alphaSlug, Gift(need: need)), HttpStatusCode.NotFound, "need_not_found");

        await ExecuteAsync("UPDATE giving.Needs SET Status = 'Closed' WHERE PublicId = @need;", new { need });
        await AssertRefusedAsync(await TryGiveAsync(donor, _alphaSlug, Gift(need: need)), HttpStatusCode.Conflict, "need_closed");

        await ExecuteAsync("UPDATE giving.Needs SET Status = 'Open', Deadline = @past WHERE PublicId = @need;", new { need, past = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue) });
        await AssertRefusedAsync(await TryGiveAsync(donor, _alphaSlug, Gift(need: need)), HttpStatusCode.Conflict, "need_closed");

        await ExecuteAsync("UPDATE giving.Needs SET Deadline = @today WHERE PublicId = @need;", new { need, today = Today.ToDateTime(TimeOnly.MinValue) });
        await AssertOkAsync(await TryGiveAsync(donor, _alphaSlug, Gift(need: need)));
    }

    // ------------------------------------------------------------------ the notification (M9)

    [Fact]
    public async Task ASignedSuccess_MakesOneDonationWithAReceipt()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        var order = await OrderAsync(donor);

        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(order)).StatusCode);

        var row = await OrderRowAsync(order);
        Assert.Equal("Paid", row.Status);
        Assert.NotNull(row.DonationId);
        Assert.Equal((short)2, row.Code);
        Assert.Equal("320025012345", row.PaymentId);
        Assert.NotNull(row.NotifiedAt);

        await using var connection = await ConnectionAsync(Platform);
        var gift = await connection.QuerySingleAsync(
            """
            SELECT d.ReceiptNo, d.InstitutionId, d.Kind, d.Amount, d.CurrencyCode, d.DonorUserId, d.RecordedBy, d.DonorName, d.DonorPhone, d.InstitutionDonorId, m.Code AS Method
            FROM giving.Donations d JOIN core.LookupValues m ON m.Id = d.PaymentMethodLookupId WHERE d.Id = @id;
            """,
            new { id = row.DonationId });

        Assert.Equal(FirstReceipt, (string)gift.ReceiptNo);
        Assert.Equal(database.AlphaInstitutionId, (int)gift.InstitutionId);
        Assert.Equal("Money", (string)gift.Kind);
        Assert.Equal(2500m, (decimal)gift.Amount);
        Assert.Equal("LKR", (string)gift.CurrencyCode);
        Assert.Equal(donor, (Guid)gift.DonorUserId);
        Assert.Equal(donor, (Guid)gift.RecordedBy);
        Assert.Equal("Nimal Perera", (string)gift.DonorName);
        Assert.Equal(await ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @donor;", new { donor }), (string)gift.DonorPhone);
        Assert.Null(gift.InstitutionDonorId);
        Assert.Equal("PAYHERE", (string)gift.Method);

        var audit = await ScalarAsync<string>("SELECT TOP (1) NewValues FROM audit.AuditLogs WHERE EntityName = 'Donation' AND EntityId = @receipt AND Action = 'Record' ORDER BY Id DESC;", new { receipt = FirstReceipt });
        Assert.Contains("PAYHERE", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Nimal", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOnlineGift_ShowsToTheDonorAndTheInstitution_AndTextsItsReceiptOnce()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        await AssertOkAsync(await NotifyAsync(await OrderAsync(donor)));

        var mine = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, "/api/me/donations"));
        Assert.Equal(FirstReceipt, mine[0].GetProperty("receiptNo").GetString());
        await AssertOkAsync(await SendAsync(Personal(donor), HttpMethod.Get, $"/api/me/donations/{mine[0].GetProperty("publicId").GetGuid()}"));

        var list = await BodyOf(await SendAsync(Alpha(_alphaStaff), HttpMethod.Get, "/api/institution/donations"));
        Assert.Equal("PAYHERE", list[0].GetProperty("method").GetString());
        Assert.False(list[0].GetProperty("recordedByMe").GetBoolean());

        await SendTextsAsync();
        await SendTextsAsync();
        var texts = await TextsToAsync(donor);
        Assert.Single(texts);
        Assert.Contains(FirstReceipt, texts[0], StringComparison.Ordinal);
        Assert.Contains("2,500.00", texts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaffVoidAnOnlineGift_AndItLeavesTheNeedAlone()
    {
        await SetAlphaMerchantAsync();
        var need = await OpenNeedAsync();
        await AssertOkAsync(await NotifyAsync(await OrderAsync(await DonorAsync(), Gift(need: need))));

        var version = (await BodyOf(await SendAsync(Alpha(_alphaOwner), HttpMethod.Get, "/api/institution/donations")))[0].GetProperty("version").GetString();
        await AssertOkAsync(await SendAsync(
            await SteppedUpAlphaAsync(),
            HttpMethod.Post,
            $"/api/institution/donations/{FirstReceipt}/void",
            new { reason = "Refunded in the PayHere portal.", version }));

        Assert.True(await ScalarAsync<bool>("SELECT IsVoided FROM giving.Donations;", new { }));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT QuantityReceived FROM giving.Needs WHERE PublicId = @need;", new { need }));
    }

    [Fact]
    public async Task EveryForgery_ChangesNothing_AndIsAnsweredAlike()
    {
        await SetAlphaMerchantAsync();
        await SetBetaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        HttpResponseMessage[] refusals =
        [
            await NotifyAsync(order, secret: "a-guessed-secret"),
            await NotifyAsync(order, secret: BetaSecret),
            await NotifyAsync(order, merchant: BetaMerchant, secret: BetaSecret),
            await NotifyAsync(order, merchant: "9999999", secret: AlphaSecret),
            await NotifyAsync(order, signature: string.Empty),
            await NotifyAsync(order, signature: Md5Sig(AlphaMerchant, order.ToString(), "2500.00", "LKR", "2", AlphaSecret).ToLowerInvariant()),
            await NotifyAsync(order, amount: "1.00"),
            await NotifyAsync(order, amount: "2500.01"),
            await NotifyAsync(order, amount: "abc"),
            await NotifyAsync(order, currency: "USD"),
            await NotifyAsync(order, status: "two"),
            await NotifyAsync(Guid.CreateVersion7()),
        ];

        foreach (var refusal in refusals)
        {
            await AssertRefusedAsync(refusal, HttpStatusCode.BadRequest, "invalid_notification");
        }

        var row = await OrderRowAsync(order);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.Code);
        Assert.Null(row.NotifiedAt);
        Assert.Equal(0, await DonationCountAsync());
    }

    [Fact]
    public async Task TheAmountIsComparedAsAFigure_NotAsText()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertOkAsync(await NotifyAsync(order, amount: "2500.0"));

        Assert.Equal("Paid", (await OrderRowAsync(order)).Status);
    }

    [Fact]
    public async Task ANotificationThatIsNotAForm_IsRefused()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertRefusedAsync(
            await SendAsync(Anonymous(), HttpMethod.Post, Notify, new { merchant_id = AlphaMerchant, order_id = order }),
            HttpStatusCode.BadRequest,
            "invalid_notification");

        using var client = Anonymous();
        using var partial = new FormUrlEncodedContent(new Dictionary<string, string> { ["order_id"] = order.ToString() });
        await AssertRefusedAsync(await client.PostAsync(Notify, partial), HttpStatusCode.BadRequest, "invalid_notification");

        Assert.Equal("Pending", (await OrderRowAsync(order)).Status);
    }

    [Fact]
    public async Task OneInstitutionsSecret_CannotSignAnothersOrder()
    {
        await SetAlphaMerchantAsync();
        await SetBetaMerchantAsync();
        var alphaOrder = await OrderAsync(await DonorAsync());
        var response = await TryGiveAsync(await DonorAsync(), _betaSlug);
        var betaOrder = (await BodyOf(response)).GetProperty("orderId").GetGuid();

        await AssertRefusedAsync(await NotifyAsync(betaOrder, secret: AlphaSecret, merchant: BetaMerchant), HttpStatusCode.BadRequest, "invalid_notification");
        await AssertOkAsync(await NotifyAsync(betaOrder, merchant: BetaMerchant, secret: BetaSecret));
        await AssertOkAsync(await NotifyAsync(alphaOrder));

        Assert.Equal(database.BetaInstitutionId, await ScalarAsync<int>("SELECT d.InstitutionId FROM giving.Donations d JOIN giving.OnlinePayments p ON p.DonationId = d.Id WHERE p.OrderId = @o;", new { o = betaOrder }));

        // Each institution's own book of receipts.
        Assert.Equal(FirstReceipt, await ReceiptOfAsync(alphaOrder));
        Assert.Equal(FirstReceipt, await ReceiptOfAsync(betaOrder));
    }

    [Fact]
    public async Task TheSameNotificationTwice_MakesOneDonation_AndTakesNoSecondNumber()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        var order = await OrderAsync(donor);

        await AssertOkAsync(await NotifyAsync(order));
        await AssertOkAsync(await NotifyAsync(order));
        await AssertOkAsync(await NotifyAsync(order, paymentId: "320025099999"));

        Assert.Equal(1, await DonationCountAsync());
        Assert.Equal("320025012345", (await OrderRowAsync(order)).PaymentId);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'DonationRecorded';", new { }));

        var next = await OrderAsync(donor);
        await AssertOkAsync(await NotifyAsync(next));
        Assert.Equal($"RCP-{Today.Year}-000002", await ReceiptOfAsync(next));
    }

    [Fact]
    public async Task TwoNotificationsAtOnce_MakeOneDonationAndOneReceipt()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());
        var id = await ScalarAsync<long>("SELECT Id FROM giving.OnlinePayments WHERE OrderId = @order;", new { order });

        await using var holder = await ConnectionAsync(Platform);
        await using var hold = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("SELECT Id FROM giving.OnlinePayments WITH (UPDLOCK, ROWLOCK) WHERE Id = @id;", new { id }, hold);

        var attempts = new[] { Task.Run(() => NotifyAsync(order)), Task.Run(() => NotifyAsync(order)) };

        await using (var observer = await ConnectionAsync(string.Empty))
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            int waiting;
            do
            {
                waiting = await observer.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM sys.dm_tran_locks l
                    JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                    WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID() AND p.object_id = OBJECT_ID('giving.OnlinePayments');
                    """);
                await Task.Delay(25);
            }
            while (waiting < attempts.Length && DateTime.UtcNow < deadline);

            Assert.True(waiting >= attempts.Length, $"Only {waiting} of {attempts.Length} notifications queued: the race was not forced.");
        }

        await hold.CommitAsync();
        var answers = await Task.WhenAll(attempts);

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        Assert.Equal(1, await DonationCountAsync());
        Assert.Equal(FirstReceipt, await ReceiptOfAsync(order));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'DonationRecorded';", new { }));
    }

    [Theory]
    [InlineData("-1", "Cancelled")]
    [InlineData("-2", "Failed")]
    [InlineData("-3", "Failed")]
    public async Task AFailure_ClosesTheOrder_AndMakesNoDonation(string status, string expected)
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertOkAsync(await NotifyAsync(order, status: status));

        var row = await OrderRowAsync(order);
        Assert.Equal(expected, row.Status);
        Assert.Equal(short.Parse(status, CultureInfo.InvariantCulture), row.Code);
        Assert.Equal("320025012345", row.PaymentId);
        Assert.NotNull(row.NotifiedAt);
        Assert.Null(row.DonationId);
        Assert.Equal(0, await DonationCountAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    public async Task AStatusThatSaysNothingNew_ChangesNothing(string status)
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertOkAsync(await NotifyAsync(order, status: status));

        var row = await OrderRowAsync(order);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.NotifiedAt);
        Assert.Equal(0, await DonationCountAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-2")]
    [InlineData("-3")]
    public async Task ALateFailure_CannotUndoAPaidOrder(string status)
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());
        await AssertOkAsync(await NotifyAsync(order));

        await AssertOkAsync(await NotifyAsync(order, status: status));

        var row = await OrderRowAsync(order);
        Assert.Equal("Paid", row.Status);
        Assert.Equal((short)2, row.Code);
        Assert.False(await ScalarAsync<bool>("SELECT IsVoided FROM giving.Donations;", new { }));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-2")]
    public async Task ASignedSuccessAfterAFailure_IsStillAGift(string status)
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());
        await AssertOkAsync(await NotifyAsync(order, status: status));

        await AssertOkAsync(await NotifyAsync(order));

        Assert.Equal("Paid", (await OrderRowAsync(order)).Status);
        Assert.Equal(1, await DonationCountAsync());
    }

    [Fact]
    public async Task AMerchantDisabledAfterTheOrder_StillGetsItsGift()
    {
        var version = await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());
        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody(secret: null, enabled: false, version: version)));

        await AssertOkAsync(await NotifyAsync(order));

        Assert.Equal("Paid", (await OrderRowAsync(order)).Status);
    }

    [Fact]
    public async Task AMerchantChangedAfterTheOrder_CannotBeChecked_SoTheOrderWaits()
    {
        var version = await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());
        await AssertOkAsync(await SendAsync(await SteppedUpAlphaAsync(), HttpMethod.Put, Merchant, MerchantBody("7777777", "the-new-merchants-secret", version: version)));

        await AssertRefusedAsync(await NotifyAsync(order), HttpStatusCode.BadRequest, "invalid_notification");
        await AssertRefusedAsync(await NotifyAsync(order, secret: "the-new-merchants-secret"), HttpStatusCode.BadRequest, "invalid_notification");

        Assert.Equal("Pending", (await OrderRowAsync(order)).Status);
    }

    [Fact]
    public async Task APaymentIdTooLongForTheColumn_DoesNotLoseTheGift()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertOkAsync(await NotifyAsync(order, paymentId: new string('9', 40)));

        var row = await OrderRowAsync(order);
        Assert.Equal("Paid", row.Status);
        Assert.Equal(new string('9', 30), row.PaymentId);
    }

    [Fact]
    public async Task AnOrderWhoseSecretCannotBeOpened_LeavesNoRowBehind()
    {
        await SetAlphaMerchantAsync();
        await ExecuteAsync("UPDATE giving.PaymentMerchants SET SecretNonce = 0x000000000000000000000000;", new { });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await TryGiveAsync(await DonorAsync(), _alphaSlug);
            response.EnsureSuccessStatusCode();
        });

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM giving.OnlinePayments;", new { }));
    }

    [Fact]
    public async Task ARunOfOrders_IsLimited()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();

        for (var i = 0; i < 30; i++)
        {
            await AssertOkAsync(await TryGiveAsync(donor, _alphaSlug));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await TryGiveAsync(donor, _alphaSlug)).StatusCode);
    }

    [Fact]
    public async Task ANeedClosedAfterTheOrder_StillGetsTheMoney_WhichCountsTowardNothing()
    {
        await SetAlphaMerchantAsync();
        var need = await OpenNeedAsync();
        var order = await OrderAsync(await DonorAsync(), Gift(need: need));
        await ExecuteAsync("UPDATE giving.Needs SET Status = 'Closed' WHERE PublicId = @need;", new { need });

        await AssertOkAsync(await NotifyAsync(order));

        Assert.Equal("Paid", (await OrderRowAsync(order)).Status);
        var needId = await ScalarAsync<int>("SELECT Id FROM giving.Needs WHERE PublicId = @need;", new { need });
        Assert.Equal(needId, await ScalarAsync<int>("SELECT NeedId FROM giving.Donations;", new { }));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT QuantityPledged FROM giving.Needs WHERE Id = @needId;", new { needId }));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT QuantityReceived FROM giving.Needs WHERE Id = @needId;", new { needId }));
    }

    // ------------------------------------------------------------------ the return page (M9)

    [Fact]
    public async Task TheReturnPage_ShowsOnlyWhatTheApiKnows()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        var order = await OrderAsync(donor);
        var path = $"/api/me/payments/{order}";

        var pending = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, path));
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal(2500m, pending.GetProperty("amount").GetDecimal());
        Assert.Equal(_alphaSlug, pending.GetProperty("institutionSlug").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("receiptNo").ValueKind);

        await AssertOkAsync(await NotifyAsync(order));

        var paid = await BodyOf(await SendAsync(Personal(donor), HttpMethod.Get, path));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal(FirstReceipt, paid.GetProperty("receiptNo").GetString());
        Assert.Equal(await ScalarAsync<Guid>("SELECT PublicId FROM giving.Donations;", new { }), paid.GetProperty("receiptPublicId").GetGuid());
    }

    [Fact]
    public async Task AnotherPersonsOrder_ReadsAsNotFound()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await AssertRefusedAsync(await SendAsync(Personal(await DonorAsync()), HttpMethod.Get, $"/api/me/payments/{order}"), HttpStatusCode.NotFound, "payment_not_found");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Anonymous(), HttpMethod.Get, $"/api/me/payments/{order}")).StatusCode);
    }

    // ------------------------------------------------------------------ RLS (M34)

    [Fact]
    public async Task AnOrder_IsReadByItsDonorAndItsInstitution_AndNoOneElse()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        await OrderAsync(donor);

        async Task<int> SeenBy(string stamp)
        {
            await using var connection = await ConnectionAsync(stamp);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM giving.OnlinePayments;");
        }

        Assert.Equal(1, await SeenBy($"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{donor}', @read_only = 1;"));
        Assert.Equal(1, await SeenBy($"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.AlphaInstitutionId}, @read_only = 1;"));
        Assert.Equal(0, await SeenBy($"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{await DonorAsync()}', @read_only = 1;"));
        Assert.Equal(0, await SeenBy($"EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = {database.BetaInstitutionId}, @read_only = 1;"));
        Assert.Equal(0, await SeenBy(string.Empty));
    }

    [Fact]
    public async Task ADonorsSession_CannotMarkTheirOwnOrderPaid_OrWriteOne()
    {
        await SetAlphaMerchantAsync();
        var donor = await DonorAsync();
        var order = await OrderAsync(donor);
        var asDonor = $"EXEC sys.sp_set_session_context @key = N'UserId', @value = '{donor}', @read_only = 1;";

        await using var connection = await ConnectionAsync(asDonor);

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("UPDATE giving.OnlinePayments SET Status = 'Failed' WHERE OrderId = @order;", new { order }));
        Assert.Contains("block predicate", update.Message, StringComparison.OrdinalIgnoreCase);

        var insert = await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync(
                """
                INSERT giving.OnlinePayments (OrderId, InstitutionId, DonorUserId, MerchantId, Amount)
                VALUES (@id, @institution, @donor, @merchant, 100);
                """,
                new { id = Guid.CreateVersion7(), institution = database.AlphaInstitutionId, donor, merchant = AlphaMerchant }));
        Assert.Contains("block predicate", insert.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Pending", (await OrderRowAsync(order)).Status);
    }

    [Fact]
    public async Task PaidNeedsItsDonation_ByTheDatabase()
    {
        await SetAlphaMerchantAsync();
        var order = await OrderAsync(await DonorAsync());

        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE giving.OnlinePayments SET Status = 'Paid' WHERE OrderId = @order;", new { order }));
    }
}

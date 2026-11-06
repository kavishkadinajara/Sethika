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
using Sethika.Application.Bookings.Sponsors;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 08, K1: the institution's donor book over HTTP, against real SQL Server. Staff add the people
/// who give regularly and change their details; a phone is in the book once; the text consent keeps
/// who ticked it and when (Q2); a confirmed account with the phone is linked; and one institution
/// never sees or changes another's donors.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them: the owner
/// holds every BOOKINGS.SPONSORS permission, staff only VIEW (K11).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class DonorBookEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "donor-book-test-signing-key-at-least-32-bytes";
    private const string Donors = "/api/institution/donors";

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;
    private Guid _alphaStaff;
    private Guid _betaOwner;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        await ExecuteAsync(
            """
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
        });
    }

    public async Task DisposeAsync()
    {
        // The reseed clears the donor book and the roles these users hold.
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

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    /// <summary>A mobile number in E.164 nobody else in the test database has.</summary>
    private static string RandomPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>The same number as a person types it here: 07X XXX XXXX.</summary>
    private static string Typed(string e164) => $"0{e164[3..5]} {e164[5..8]} {e164[8..]}";

    private async Task<Guid> UserAsync(string userType, string? phone = null, bool confirmed = true, bool deleted = false)
    {
        var id = Guid.CreateVersion7();
        phone ??= RandomPhone();

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp, IsDeleted)
            VALUES (@id, @userType, N'Donor Book Test', @phone, @confirmed, 'en', 'Active', 'stamp', @deleted);
            """,
            new { id, userType, phone, confirmed, deleted });

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

    private HttpClient ClientFor(Guid userId, int institutionId)
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

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("invalid_details", body.GetProperty("code").GetString());
        Assert.Equal(fields, body.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    private static object DonorBody(string phone, string name = "Sunil Perera", bool consent = false, string? version = null, string? language = null, string? notes = null) =>
        new { fullName = name, phone, notes, language, textConsent = consent, version };

    /// <summary>Adds a donor as Alpha's owner: its id and version.</summary>
    private async Task<(Guid Id, string Version)> AddAsync(string phone, bool consent = false)
    {
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, DonorBody(phone, consent: consent));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyOf(response);
        return (body.GetProperty("publicId").GetGuid(), body.GetProperty("version").GetString()!);
    }

    private static async Task<JsonElement[]> BookAsync(HttpClient client)
    {
        var response = await SendAsync(client, HttpMethod.Get, Donors);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await BodyOf(response)).EnumerateArray()];
    }

    private async Task<JsonElement> EntryAsync(Guid publicId) =>
        Assert.Single(await BookAsync(Alpha(_alphaOwner)), d => d.GetProperty("publicId").GetGuid() == publicId);

    private sealed record ConsentRow(DateTime? TextConsentAt, Guid? TextConsentBy);

    private async Task<ConsentRow> ConsentAsync(Guid publicId)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<ConsentRow>(
            "SELECT TextConsentAt, TextConsentBy FROM org.InstitutionDonors WHERE PublicId = @publicId;", new { publicId });
    }

    private Task<Guid?> LinkedAsync(Guid publicId) =>
        ScalarAsync<Guid?>("SELECT UserId FROM org.InstitutionDonors WHERE PublicId = @publicId;", new { publicId });

    // ------------------------------------------------------------------ adding and listing

    [Fact]
    public async Task TheOwnerAddsADonorAndTheBookListsThem()
    {
        var phone = RandomPhone();
        var response = await SendAsync(
            Alpha(_alphaOwner),
            HttpMethod.Post,
            Donors,
            DonorBody(Typed(phone), name: "  Sunil Perera  ", consent: true, language: "ta", notes: " Gives on the 5th "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await BodyOf(response);
        var id = created.GetProperty("publicId").GetGuid();

        var donor = await EntryAsync(id);
        Assert.Equal("Sunil Perera", donor.GetProperty("fullName").GetString());
        Assert.Equal(phone, donor.GetProperty("phone").GetString());
        Assert.Equal("Gives on the 5th", donor.GetProperty("notes").GetString());
        Assert.Equal("ta", donor.GetProperty("language").GetString());
        Assert.False(donor.GetProperty("hasAccount").GetBoolean());
        Assert.True(donor.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, donor.GetProperty("activeSponsorships").GetInt32());
        Assert.Equal(created.GetProperty("version").GetString(), donor.GetProperty("version").GetString());

        // Who added them and who recorded the consent is the owner, not anyone named in the request.
        Assert.Equal(_alphaOwner, await ScalarAsync<Guid>("SELECT CreatedBy FROM org.InstitutionDonors WHERE PublicId = @id;", new { id }));
        var consent = await ConsentAsync(id);
        Assert.Equal(_alphaOwner, consent.TextConsentBy);
        Assert.NotNull(consent.TextConsentAt);
        Assert.Equal(
            DateTime.SpecifyKind(consent.TextConsentAt!.Value, DateTimeKind.Utc),
            donor.GetProperty("textConsentAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task TextsAreInSinhalaAndOffUnlessTicked()
    {
        // Neither sent: the form's defaults.
        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, new { fullName = "Sunil Perera", phone = RandomPhone() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await BodyOf(response)).GetProperty("publicId").GetGuid();

        var donor = await EntryAsync(id);
        Assert.Equal("si", donor.GetProperty("language").GetString());
        Assert.Equal(JsonValueKind.Null, donor.GetProperty("textConsentAt").ValueKind);
        Assert.Equal(new ConsentRow(null, null), await ConsentAsync(id));
    }

    [Fact]
    public async Task TheBookCountsEachDonorsRunningSponsorships()
    {
        var (id, _) = await AddAsync(RandomPhone());
        var (other, _) = await AddAsync(RandomPhone());

        await ExecuteAsync(
            """
            INSERT booking.StandingSponsorships
                (PublicId, InstitutionId, InstitutionDonorId, MealSlotId, Pattern, StartDate, EndDate, Headcount, Status, EndedAt, EndedBy, CreatedBy)
            SELECT NEWID(), d.InstitutionId, d.Id, s.Id, 'Daily', '2026-12-01', NULL, 10, v.Status,
                   CASE WHEN v.Status = 'Ended' THEN SYSUTCDATETIME() END, CASE WHEN v.Status = 'Ended' THEN @by END, @by
            FROM org.InstitutionDonors d
            JOIN booking.MealSlots s ON s.InstitutionId = d.InstitutionId
            CROSS JOIN (VALUES ('Active'), ('Active'), ('Ended')) AS v(Status)
            WHERE d.PublicId = @id;
            """,
            new { id, by = _alphaOwner });

        Assert.Equal(2, (await EntryAsync(id)).GetProperty("activeSponsorships").GetInt32());
        Assert.Equal(0, (await EntryAsync(other)).GetProperty("activeSponsorships").GetInt32());
    }

    // ------------------------------------------------------------------ the consent (Q2)

    [Fact]
    public async Task TheConsentKeepsWhoFirstTickedItAndUntickingForgetsIt()
    {
        var phone = RandomPhone();
        var (id, version) = await AddAsync(phone);
        var secondOwner = await MemberAsync(database.AlphaInstitutionId, "OWNER");

        var ticked = await SendAsync(Alpha(secondOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, consent: true, version: version));
        Assert.Equal(HttpStatusCode.OK, ticked.StatusCode);
        var first = await ConsentAsync(id);
        Assert.Equal(secondOwner, first.TextConsentBy);
        Assert.NotNull(first.TextConsentAt);

        // Ticked again by someone else: still the first who and when.
        version = (await BodyOf(ticked)).GetProperty("version").GetString()!;
        var again = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, consent: true, version: version));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first, await ConsentAsync(id));

        version = (await BodyOf(again)).GetProperty("version").GetString()!;
        var unticked = await SendAsync(Alpha(secondOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, consent: false, version: version));
        Assert.Equal(HttpStatusCode.OK, unticked.StatusCode);
        Assert.Equal(new ConsentRow(null, null), await ConsentAsync(id));
        Assert.Equal(secondOwner, await ScalarAsync<Guid>("SELECT UpdatedBy FROM org.InstitutionDonors WHERE PublicId = @id;", new { id }));
    }

    // ------------------------------------------------------------------ linking an account

    [Fact]
    public async Task AConfirmedAccountWithThePhoneIsLinked()
    {
        var phone = RandomPhone();
        var account = await UserAsync("Donor", phone);

        var (id, _) = await AddAsync(Typed(phone));

        Assert.Equal(account, await LinkedAsync(id));
        Assert.True((await EntryAsync(id)).GetProperty("hasAccount").GetBoolean());
    }

    [Fact]
    public async Task AnUnconfirmedOrDeletedAccountIsNotLinked()
    {
        var unconfirmed = RandomPhone();
        await UserAsync("Donor", unconfirmed, confirmed: false);
        var deleted = RandomPhone();
        await UserAsync("Donor", deleted, deleted: true);

        var (first, _) = await AddAsync(unconfirmed);
        var (second, _) = await AddAsync(deleted);

        Assert.Null(await LinkedAsync(first));
        Assert.Null(await LinkedAsync(second));
        Assert.False((await EntryAsync(first)).GetProperty("hasAccount").GetBoolean());
    }

    [Fact]
    public async Task ChangingThePhoneMovesTheLink()
    {
        var phone = RandomPhone();
        var account = await UserAsync("Donor", phone);
        var (id, version) = await AddAsync(phone);
        Assert.Equal(account, await LinkedAsync(id));

        var moved = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(RandomPhone(), version: version));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Null(await LinkedAsync(id));

        version = (await BodyOf(moved)).GetProperty("version").GetString()!;
        var back = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, version: version));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal(account, await LinkedAsync(id));
    }

    [Fact]
    public async Task AnAccountIsOneDonorAtMost()
    {
        // The account's phone changes after it was linked; a new entry for the new phone would link
        // the same account a second time.
        var phone = RandomPhone();
        var account = await UserAsync("Donor", phone);
        await AddAsync(phone);

        var changed = RandomPhone();
        await ExecuteAsync("UPDATE auth.Users SET PhoneNumber = @changed WHERE Id = @account;", new { changed, account });

        var response = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, DonorBody(changed));
        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "donor_account_taken");
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task TheFormIsChecked()
    {
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, new { }),
            "fullName", "phone");

        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, DonorBody("12345", name: new string('a', 151), language: "fr", notes: new string('n', 501))),
            "fullName", "phone", "notes", "language");

        // The longest of each is taken.
        var longest = await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, DonorBody(RandomPhone(), name: new string('a', 150), language: "en", notes: new string('n', 500)));
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);

        // An edit without the version it was read at is refused, before anything else.
        var (id, _) = await AddAsync(RandomPhone());
        await AssertFieldsAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(RandomPhone(), name: "")),
            "version", "fullName");

        Assert.Equal(2, (await BookAsync(Alpha(_alphaOwner))).Length);
    }

    [Fact]
    public async Task APhoneIsInTheBookOnce()
    {
        var phone = RandomPhone();
        await AddAsync(phone);

        // However it is typed.
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Post, Donors, DonorBody(Typed(phone))),
            HttpStatusCode.Conflict,
            "donor_phone_taken");

        var (other, version) = await AddAsync(RandomPhone());
        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{other}", DonorBody(phone, version: version)),
            HttpStatusCode.Conflict,
            "donor_phone_taken");

        // Once per institution: Beta keeps its own book.
        var beta = await SendAsync(Beta(_betaOwner), HttpMethod.Post, Donors, DonorBody(phone));
        Assert.Equal(HttpStatusCode.Created, beta.StatusCode);
    }

    [Fact]
    public async Task AnEditMadeOnAnOldReadIsRefused()
    {
        var phone = RandomPhone();
        var (id, version) = await AddAsync(phone);

        var first = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, name: "First Edit", version: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, name: "Second Edit", version: version));
        await AssertRefusedAsync(second, HttpStatusCode.Conflict, "stale");
        Assert.Equal("First Edit", (await EntryAsync(id)).GetProperty("fullName").GetString());
    }

    // ------------------------------------------------------------------ who may

    [Fact]
    public async Task StaffSeeTheBookButCannotChangeIt()
    {
        var phone = RandomPhone();
        var (id, version) = await AddAsync(phone);

        Assert.Single(await BookAsync(Alpha(_alphaStaff)));

        var added = await SendAsync(Alpha(_alphaStaff), HttpMethod.Post, Donors, DonorBody(RandomPhone()));
        Assert.Equal(HttpStatusCode.Forbidden, added.StatusCode);

        var changed = await SendAsync(Alpha(_alphaStaff), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, name: "Changed", version: version));
        Assert.Equal(HttpStatusCode.Forbidden, changed.StatusCode);
        Assert.Equal("Sunil Perera", (await EntryAsync(id)).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task AnotherInstitutionNeitherSeesNorChangesTheDonor()
    {
        var phone = RandomPhone();
        var (id, version) = await AddAsync(phone);
        var added = await SendAsync(Beta(_betaOwner), HttpMethod.Post, Donors, DonorBody(RandomPhone(), name: "Beta Donor"));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var betaDonor = (await BodyOf(added)).GetProperty("publicId").GetGuid();

        Assert.Equal([betaDonor], (await BookAsync(Beta(_betaOwner))).Select(d => d.GetProperty("publicId").GetGuid()));
        Assert.Equal([id], (await BookAsync(Alpha(_alphaOwner))).Select(d => d.GetProperty("publicId").GetGuid()));

        await AssertRefusedAsync(
            await SendAsync(Beta(_betaOwner), HttpMethod.Put, $"{Donors}/{id}", DonorBody(phone, name: "Taken Over", version: version)),
            HttpStatusCode.NotFound,
            "donor_not_found");
        Assert.Equal("Sunil Perera", (await EntryAsync(id)).GetProperty("fullName").GetString());

        await AssertRefusedAsync(
            await SendAsync(Alpha(_alphaOwner), HttpMethod.Put, $"{Donors}/{Guid.CreateVersion7()}", DonorBody(phone, version: version)),
            HttpStatusCode.NotFound,
            "donor_not_found");
    }

    [Fact]
    public async Task WithThePlatformsReachTheHandlerAndTheQueryStillTouchOneInstitution()
    {
        // Row-Level Security alone would hide another institution's donor. A platform connection sees
        // every row, so this shows the handler and the query name the institution themselves.
        var phone = RandomPhone();
        var (alphaDonor, version) = await AddAsync(phone);
        var added = await SendAsync(Beta(_betaOwner), HttpMethod.Post, Donors, DonorBody(RandomPhone(), name: "Beta Donor"));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var platform = TestCurrentUserContext.Platform();
        var queries = new DonorBookQueries(new SqlConnectionFactory(database.ConnectionString, platform));
        Assert.Equal([alphaDonor], (await queries.ListDonorsAsync(database.AlphaInstitutionId)).Select(d => d.PublicId));

        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(platform))
            .Options);
        var handler = new DonorBookHandler(context, new PhoneNumberNormalizer(), TimeProvider.System);
        var result = await handler.UpdateAsync(
            database.BetaInstitutionId,
            _betaOwner,
            alphaDonor,
            new DonorInput("Taken Over", phone, null, null, false, Convert.FromBase64String(version)));

        Assert.Equal(DonorChangeResult.DonorNotFound, result.Error);
        Assert.Equal("Sunil Perera", (await EntryAsync(alphaDonor)).GetProperty("fullName").GetString());
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using Sethika.Application.Bookings.Sponsors;
using Sethika.Domain.Access;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 08, K24 and K14 (K35-K37): what needed the platform's reach, against real SQL Server and Redis.
/// A linked standing sponsor lets one of their own days go from My bookings, by the staff skip's rules;
/// a person's sign-up or sign-in links them to every donor book that has their confirmed phone, and the
/// standing days already booked become theirs; a staff change of link carries those days with it.
/// </summary>
/// <remarks>
/// Alpha's and Beta's members hold the template roles, cloned as onboarding clones them. Slots need
/// approval and take one sponsor a day, with a window from tomorrow (<c>MinAdvanceDays = 1</c>).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class StandingSponsorLinkTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "standing-link-test-signing-key-at-least-32-bytes";
    private const string Password = "Dana!2026Sethika";
    private const string Sponsorships = "/api/institution/sponsorships";

    private readonly ConcurrentQueue<(string Phone, string Message)> _sms = new();
    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    private Guid _alphaOwner;

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
            """,
            new { alpha = database.AlphaInstitutionId, beta = database.BetaInstitutionId });

        _alphaOwner = await UserAsync("InstitutionStaff");
        await ExecuteAsync(
            """
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';
            """,
            new { id = _alphaOwner, alpha = database.AlphaInstitutionId });

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStandingBookingWake>(new NoWake());
                services.AddScoped<ISmsSender>(_ => new RecordingSmsSender(_sms));
            });
        });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
            INSERT @ids SELECT Id FROM auth.Users WHERE Id IN @users OR PhoneNumber IN @phones;

            DELETE FROM notify.Notifications WHERE Recipient IN @phones;
            DELETE FROM core.OutboxMessages WHERE AggregateType = 'User'
                AND AggregateId IN (SELECT LOWER(CONVERT(VARCHAR(36), Id)) FROM @ids);
            DELETE FROM auth.UserConsents  WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.DonorProfiles WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
            DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
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

    private sealed class RecordingSmsSender(ConcurrentQueue<(string Phone, string Message)> sent) : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, string? dedupKey = null, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((phoneNumber, message));
            return Task.CompletedTask;
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

    private string RandomPhone()
    {
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    /// <summary>An account; a donor (the global DONOR role) unless <paramref name="userType"/> says otherwise.</summary>
    private async Task<Guid> UserAsync(string userType = "Donor", string? phone = null, bool confirmed = true, bool deleted = false, bool withPassword = false)
    {
        var id = Guid.CreateVersion7();
        phone ??= RandomPhone();
        var hash = withPassword ? _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password) : null;
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status, SecurityStamp, IsDeleted)
            VALUES (@id, @userType, N'Link Test', @phone, @confirmed, @hash, 'en', 'Active', 'stamp', @deleted);
            IF @userType = 'Donor'
                INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
                SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, userType, phone, confirmed, hash, deleted });

        _users.Add(id);
        return id;
    }

    private Task<string> PhoneOfAsync(Guid userId) =>
        ScalarAsync<string>("SELECT PhoneNumber FROM auth.Users WHERE Id = @userId;", new { userId });

    /// <summary>A person in <paramref name="institutionId"/>'s donor book (Alpha's by default), linked to <paramref name="userId"/> if given.</summary>
    private async Task<(Guid PublicId, string Phone)> DonorAsync(Guid? userId = null, string? phone = null, int? institutionId = null)
    {
        var publicId = Guid.CreateVersion7();
        phone ??= RandomPhone();
        await ExecuteAsync(
            """
            INSERT org.InstitutionDonors (PublicId, InstitutionId, UserId, FullName, Phone, Language, TextConsentAt, TextConsentBy, CreatedBy)
            VALUES (@publicId, @institutionId, @userId, N'Sunil Perera', @phone, 'en', SYSUTCDATETIME(), @createdBy, @createdBy);
            """,
            new { publicId, institutionId = institutionId ?? database.AlphaInstitutionId, userId, phone, createdBy = SethikaTestDatabase.AlphaUserId });

        return (publicId, phone);
    }

    private Task<Guid?> LinkedAccountAsync(Guid donor) =>
        ScalarAsync<Guid?>("SELECT UserId FROM org.InstitutionDonors WHERE PublicId = @donor;", new { donor });

    /// <summary>A lunch at Alpha every day, approval needed, one sponsor a day, a 1-30 day window.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync()
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @institutionId, @name, '11:30', '13:00', 'Exclusive', 1, 1, 200, 1, 30, 1,
                    'DonorBrings,InstitutionPrepares', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { publicId, institutionId = database.AlphaInstitutionId, name = "Link " + Guid.NewGuid().ToString("N")[..8], createdBy = SethikaTestDatabase.AlphaUserId });

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

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Sponsorship(Guid PublicId, int Id);

    /// <summary>Every <paramref name="day"/>'s weekday for three weeks, set up by Alpha's owner: four booked days.</summary>
    private async Task<Sponsorship> WeeklyAsync(Guid donor, Guid slot, DateOnly day)
    {
        var response = await SendAsync(Alpha(), HttpMethod.Post, Sponsorships, new Dictionary<string, object?>
        {
            ["donor"] = donor,
            ["mealSlot"] = slot,
            ["pattern"] = StandingPattern.Weekly,
            ["weekDays"] = new[] { (int)day.DayOfWeek },
            ["startDate"] = Iso(day),
            ["endDate"] = Iso(day.AddDays(21)),
            ["headcount"] = 25,
            ["fulfilment"] = FulfilmentMode.InstitutionPrepares,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var publicId = (await BodyOf(response)).GetProperty("publicId").GetGuid();
        return new Sponsorship(publicId, await ScalarAsync<int>("SELECT Id FROM booking.StandingSponsorships WHERE PublicId = @publicId;", new { publicId }));
    }

    private sealed class BookingRow
    {
        public long Id { get; init; }
        public string BookingNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public Guid? DonorUserId { get; init; }
        public string? CancelReason { get; init; }
        public Guid? CancelledBy { get; init; }
        public DateTime SlotDate { get; init; }

        public DateOnly Date => DateOnly.FromDateTime(SlotDate);
    }

    private async Task<List<BookingRow>> BookingsOfAsync(int sponsorshipId)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<BookingRow>(
            """
            SELECT b.Id, b.BookingNo, b.Status, b.DonorUserId, b.CancelReason, b.CancelledBy, si.SlotDate
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

    private async Task<List<DayChangeRow>> DayChangesAsync(int sponsorshipId, DateOnly date)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<DayChangeRow>(
            """
            SELECT Kind, BookingId, Reason, CreatedBy FROM booking.StandingExceptions
            WHERE StandingSponsorshipId = @sponsorshipId AND ExceptionDate = @date;
            """,
            new { sponsorshipId, date = date.ToDateTime(TimeOnly.MinValue) })];
    }

    private SqlConnectionFactory PlatformConnections() => new(database.ConnectionString, TestCurrentUserContext.Platform());

    /// <summary>Sends every standing text waiting in the outbox through the real handler and templates.</summary>
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
    }

    private async Task<List<string>> DedupKeysToAsync(string phone)
    {
        await using var connection = await PlatformConnectionAsync();
        return [.. await connection.QueryAsync<string>("SELECT DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;", new { phone })];
    }

    private Task<int> HintsAsync(DateOnly date) =>
        ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM core.OutboxMessages
            WHERE EventType = 'AvailabilityChanged' AND ProcessedAt IS NULL AND AggregateId = @institution
              AND JSON_VALUE(Payload, '$.from') = @date;
            """,
            new { institution = database.AlphaInstitutionId.ToString(CultureInfo.InvariantCulture), date = Iso(date) });

    private static string SkipPath(string bookingNo) => $"/api/bookings/{bookingNo}/skip";

    /// <summary>A linked sponsor with a weekly standing lunch three days from now: the slot, the sponsorship, its first day and that day's booking.</summary>
    private async Task<(Guid Sponsor, string Phone, int SlotId, Sponsorship Standing, DateOnly Day, BookingRow Booking)> LinkedSponsorAsync()
    {
        var sponsor = await UserAsync();
        var phone = await PhoneOfAsync(sponsor);
        var (donor, _) = await DonorAsync(sponsor, phone);
        var (slotId, slot) = await SlotAsync();
        var day = Today.AddDays(3);
        var standing = await WeeklyAsync(donor, slot, day);

        var booking = Assert.Single(await BookingsOfAsync(standing.Id), b => b.Date == day);
        Assert.Equal((BookingStatus.Approved, (Guid?)sponsor), (booking.Status, booking.DonorUserId));
        return (sponsor, phone, slotId, standing, day, booking);
    }

    // ------------------------------------------------------------------ K24: the sponsor's own skip

    [Fact]
    public async Task ALinkedSponsor_LetsTheirOwnDayGo_AsStaffWould_AndIsTextedOnce()
    {
        var (sponsor, phone, slotId, standing, day, booking) = await LinkedSponsorAsync();
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));
        var hints = await HintsAsync(day);

        var response = await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo), new { reason = "  Away in Kandy  " });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The booking cancelled with their reason and by them, the place given back, the day recorded as theirs.
        var after = Assert.Single(await BookingsOfAsync(standing.Id), b => b.Id == booking.Id);
        Assert.Equal((BookingStatus.Cancelled, "Away in Kandy", (Guid?)sponsor), (after.Status, after.CancelReason, after.CancelledBy));
        Assert.Equal(0, await BookedSponsorsAsync(slotId, day));
        Assert.Equal([new DayChangeRow(StandingDayChange.Skipped, booking.Id, "Away in Kandy", sponsor)], await DayChangesAsync(standing.Id, day));
        Assert.Equal(hints + 1, await HintsAsync(day));

        // The other days stay theirs.
        Assert.Equal(3, (await BookingsOfAsync(standing.Id)).Count(b => b.Status == BookingStatus.Approved));

        // Once: a second skip is refused, and the text goes once however often the outbox runs.
        await AssertRefusedAsync(
            await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo)),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.DayAlreadyChanged);

        await SendTextsAsync();
        await SendTextsAsync();
        Assert.Single(await DedupKeysToAsync(phone), k => k.StartsWith(StandingTexts.Skipped, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASkip_WithoutAReason_ReadsAsTheSponsorCannotGive()
    {
        var (sponsor, _, _, standing, day, booking) = await LinkedSponsorAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo))).StatusCode);

        Assert.Equal(StandingSponsorshipHandler.SkippedReason, Assert.Single(await BookingsOfAsync(standing.Id), b => b.Id == booking.Id).CancelReason);
        Assert.Null(Assert.Single(await DayChangesAsync(standing.Id, day)).Reason);
    }

    [Fact]
    public async Task OnlyTheSponsorsOwnStandingBooking_CanBeLetGo()
    {
        var (sponsor, _, slotId, standing, day, booking) = await LinkedSponsorAsync();

        // Another donor naming the sponsor's booking.
        await AssertRefusedAsync(
            await SendAsync(ClientFor(await UserAsync(), null), HttpMethod.Post, SkipPath(booking.BookingNo)),
            HttpStatusCode.NotFound,
            "booking_not_found");

        // The sponsor's own ordinary booking is cancelled, not skipped.
        var (_, otherSlot) = await SlotAsync();
        var own = await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, "/api/bookings",
            new { mealSlot = otherSlot, date = Iso(day), headcount = 10, fulfilment = FulfilmentMode.DonorBrings });
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        var ownNo = (await BodyOf(own)).GetProperty("bookingNo").GetString()!;
        await AssertRefusedAsync(
            await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(ownNo)),
            HttpStatusCode.NotFound,
            "booking_not_found");

        // A booking of their arrangement that is not theirs in the app (no account on it).
        await ExecuteAsync("UPDATE booking.Bookings SET DonorUserId = NULL WHERE Id = @id;", new { id = booking.Id });
        await AssertRefusedAsync(
            await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo)),
            HttpStatusCode.NotFound,
            "booking_not_found");
        await ExecuteAsync("UPDATE booking.Bookings SET DonorUserId = @sponsor WHERE Id = @id;", new { id = booking.Id, sponsor });

        // The donor book row no longer linked to them (staff changed it): the booking is still theirs, the arrangement is not.
        await ExecuteAsync("UPDATE org.InstitutionDonors SET UserId = NULL WHERE Id = (SELECT InstitutionDonorId FROM booking.StandingSponsorships WHERE Id = @id);", new { id = standing.Id });
        await AssertRefusedAsync(
            await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo)),
            HttpStatusCode.NotFound,
            "booking_not_found");

        // None of it changed anything.
        Assert.Empty(await DayChangesAsync(standing.Id, day));
        Assert.Equal(1, await BookedSponsorsAsync(slotId, day));
    }

    [Fact]
    public async Task AnEndedSponsorship_RefusesTheSkip()
    {
        var (sponsor, _, _, standing, _, booking) = await LinkedSponsorAsync();
        var version = Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM booking.StandingSponsorships WHERE Id = @id;", new { id = standing.Id }));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/{standing.PublicId}/end", new { version })).StatusCode);

        await AssertRefusedAsync(
            await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo)),
            HttpStatusCode.Conflict,
            SponsorshipChangeResult.Ended);
    }

    [Fact]
    public async Task AReasonTooLong_IsRefused_AndNothingChanges()
    {
        var (sponsor, _, _, standing, day, booking) = await LinkedSponsorAsync();

        var response = await SendAsync(ClientFor(sponsor, null), HttpMethod.Post, SkipPath(booking.BookingNo), new { reason = new string('x', StandingDayChange.MaxReason + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["reason"], (await BodyOf(response)).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.Empty(await DayChangesAsync(standing.Id, day));
    }

    [Fact]
    public async Task AnInstitutionSession_CannotSkipAsADonor()
    {
        var (_, _, _, _, _, booking) = await LinkedSponsorAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Alpha(), HttpMethod.Post, SkipPath(booking.BookingNo))).StatusCode);
    }

    // ------------------------------------------------------------------ K14: linking at sign-up and sign-in

    /// <summary>A real code sent to <paramref name="phone"/> for <paramref name="purpose"/>, read from the recorded SMS.</summary>
    private string LastCodeTo(string phone) =>
        new([.. _sms.Last(m => m.Phone == phone).Message.Where(char.IsDigit)]);

    [Fact]
    public async Task SigningUp_LinksEveryDonorBookWithThePhone_AndTheirStandingDaysBecomeTheirs()
    {
        var phone = RandomPhone();
        var (alphaDonor, _) = await DonorAsync(phone: phone);
        var (betaDonor, _) = await DonorAsync(phone: phone, institutionId: database.BetaInstitutionId);
        var (_, slot) = await SlotAsync();
        var day = Today.AddDays(3);
        var standing = await WeeklyAsync(alphaDonor, slot, day);

        // A day the institution let go before: it stays as it was.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/{standing.PublicId}/skip", new { date = Iso(day) })).StatusCode);
        Assert.All(await BookingsOfAsync(standing.Id), b => Assert.Null(b.DonorUserId));

        string ticket;
        using (var scope = _host.Services.CreateScope())
        {
            var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
            Assert.True((await otp.SendAsync(phone, OtpPurpose.Signup, "en")).WasSent);
            ticket = (await otp.VerifyAsync(phone, OtpPurpose.Signup, LastCodeTo(phone))).Ticket!;
        }

        var hashes = await ScalarAsync<string>(
            "SELECT STRING_AGG(ContentSha256, ',') FROM core.LegalDocuments WHERE IsCurrent = 1 AND Language = 'en' AND Code IN ('TERMS', 'PRIVACY');",
            new { });
        SignUpDonorResult signedUp;
        using (var scope = _host.Services.CreateScope())
        {
            signedUp = await scope.ServiceProvider.GetRequiredService<SignUpDonorHandler>().HandleAsync(
                new SignUpDonorCommand(phone, ticket, "Sunil Perera", null, Password, "en", hashes.Split(','), "203.0.113.9", "xunit"));
        }

        Assert.True(signedUp.Succeeded, signedUp.ToString());
        var account = signedUp.UserId!.Value;
        _users.Add(account);

        Assert.Equal(account, await LinkedAccountAsync(alphaDonor));
        Assert.Equal(account, await LinkedAccountAsync(betaDonor));

        var bookings = await BookingsOfAsync(standing.Id);
        Assert.Equal(3, bookings.Count(b => b.Status == BookingStatus.Approved && b.DonorUserId == account));
        Assert.Null(Assert.Single(bookings, b => b.Status == BookingStatus.Cancelled).DonorUserId);

        // And they are in My bookings, marked as regular days.
        var mine = (await BodyOf(await SendAsync(ClientFor(account, null), HttpMethod.Get, "/api/bookings/mine"))).EnumerateArray().ToList();
        Assert.Equal(3, mine.Count(b => b.GetProperty("regular").GetBoolean()));
    }

    [Fact]
    public async Task SigningInWithAPassword_Links()
    {
        var account = await UserAsync(withPassword: true);
        var phone = await PhoneOfAsync(account);
        var (donor, _) = await DonorAsync(phone: phone);

        using (var scope = _host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<SignInHandler>().HandleAsync(new SignInCommand(phone, Password, "203.0.113.7", "xunit"));
            Assert.True(result.Succeeded);
        }

        Assert.Equal(account, await LinkedAccountAsync(donor));
    }

    [Fact]
    public async Task AWrongPassword_LinksNothing()
    {
        var account = await UserAsync(withPassword: true);
        var (donor, _) = await DonorAsync(phone: await PhoneOfAsync(account));

        using (var scope = _host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<SignInHandler>().HandleAsync(new SignInCommand(await PhoneOfAsync(account), Password + "x", "203.0.113.7", "xunit"));
            Assert.False(result.Succeeded);
        }

        Assert.Null(await LinkedAccountAsync(donor));
    }

    [Fact]
    public async Task SigningInWithACode_Links()
    {
        var account = await UserAsync();
        var phone = await PhoneOfAsync(account);
        var (donor, _) = await DonorAsync(phone: phone);

        using (var scope = _host.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<OtpSignInHandler>();
            await handler.RequestAsync(phone);
            var result = await handler.SignInAsync(new OtpSignInCommand(phone, LastCodeTo(phone), "203.0.113.7", "xunit"));
            Assert.True(result.Succeeded);
        }

        Assert.Equal(account, await LinkedAccountAsync(donor));
    }

    private async Task<int> LinkAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDonorBookLinks>().LinkAsync(userId);
    }

    [Fact]
    public async Task AnUnconfirmedPhone_OrADeletedAccount_LinksNothing()
    {
        var unconfirmed = await UserAsync(confirmed: false);
        var (first, _) = await DonorAsync(phone: await PhoneOfAsync(unconfirmed));
        Assert.Equal(0, await LinkAsync(unconfirmed));
        Assert.Null(await LinkedAccountAsync(first));

        var deleted = await UserAsync(deleted: true);
        var (second, _) = await DonorAsync(phone: await PhoneOfAsync(deleted));
        Assert.Equal(0, await LinkAsync(deleted));
        Assert.Null(await LinkedAccountAsync(second));
    }

    [Fact]
    public async Task ARowStaffLinked_StaysAsTheyLeftIt_AndAnInstitutionGetsOneRowPerAccount()
    {
        // A row already linked to someone else is not taken over.
        var other = await UserAsync();
        var account = await UserAsync();
        var phone = await PhoneOfAsync(account);
        var (taken, _) = await DonorAsync(other, phone);
        Assert.Equal(0, await LinkAsync(account));
        Assert.Equal(other, await LinkedAccountAsync(taken));

        // Already Beta's donor under another phone: Beta's row with this phone stays unlinked, Alpha's is linked.
        var second = await UserAsync();
        var secondPhone = await PhoneOfAsync(second);
        await DonorAsync(second, institutionId: database.BetaInstitutionId);
        var (betaRow, _) = await DonorAsync(phone: secondPhone, institutionId: database.BetaInstitutionId);
        var (alphaRow, _) = await DonorAsync(phone: secondPhone);

        Assert.Equal(1, await LinkAsync(second));
        Assert.Null(await LinkedAccountAsync(betaRow));
        Assert.Equal(second, await LinkedAccountAsync(alphaRow));

        // Again: nothing more to link.
        Assert.Equal(0, await LinkAsync(second));
    }

    // ------------------------------------------------------------------ K37: staff change the link

    private static object DonorBody(string phone, string version) =>
        new { fullName = "Sunil Perera", phone, textConsent = true, version };

    private async Task<string> VersionOfAsync(Guid donor) =>
        Convert.ToBase64String(await ScalarAsync<byte[]>("SELECT RowVersion FROM org.InstitutionDonors WHERE PublicId = @donor;", new { donor }));

    [Fact]
    public async Task ChangingADonorsPhone_CarriesTheirStandingDaysToTheAccountNowLinked_AndBack()
    {
        var (donor, oldPhone) = await DonorAsync();
        var (_, slot) = await SlotAsync();
        var day = Today.AddDays(3);
        var standing = await WeeklyAsync(donor, slot, day);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(Alpha(), HttpMethod.Post, $"{Sponsorships}/{standing.PublicId}/skip", new { date = Iso(day) })).StatusCode);

        var account = await UserAsync();
        var linked = await SendAsync(Alpha(), HttpMethod.Put, $"/api/institution/donors/{donor}", DonorBody(await PhoneOfAsync(account), await VersionOfAsync(donor)));
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(account, await LinkedAccountAsync(donor));

        var bookings = await BookingsOfAsync(standing.Id);
        Assert.Equal(3, bookings.Count(b => b.Status == BookingStatus.Approved && b.DonorUserId == account));
        Assert.Null(Assert.Single(bookings, b => b.Status == BookingStatus.Cancelled).DonorUserId);

        // A stale editor changes neither the row nor the days.
        var stale = await SendAsync(Alpha(), HttpMethod.Put, $"/api/institution/donors/{donor}", DonorBody(oldPhone, Convert.ToBase64String(new byte[8])));
        await AssertRefusedAsync(stale, HttpStatusCode.Conflict, "stale");
        Assert.Equal(3, (await BookingsOfAsync(standing.Id)).Count(b => b.DonorUserId == account));

        // Back to a phone with no account: the days are nobody's in the app again.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Alpha(), HttpMethod.Put, $"/api/institution/donors/{donor}", DonorBody(oldPhone, await VersionOfAsync(donor)))).StatusCode);
        Assert.All(await BookingsOfAsync(standing.Id), b => Assert.Null(b.DonorUserId));
    }
}

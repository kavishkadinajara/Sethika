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
using Sethika.Application.Bookings.Sharing;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 07, C.4: co-sponsors against real SQL Server. A donor invites people to share a booking, the
/// link shows its summary to anyone, a signed-in person joins or declines it once, the donor withdraws
/// what nobody answered, and the joined see the booking and its reminder - while Row-Level Security
/// shows each row to the institution, the person who answered and the donor who invited, nobody else.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class CoSponsorTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "co-sponsor-test-signing-key-at-least-32-bytes";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;
    private Guid? _owner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;

            -- Alpha's OWNER role, cloned from the template as onboarding's approval clones it.
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            SELECT @alpha, 'Institution', t.RoleLevelId, t.Code, t.Name, 0
            FROM auth.Roles t WHERE t.IsTemplate = 1 AND t.InstitutionId IS NULL AND t.Code = 'OWNER';

            INSERT auth.RolePermissions (RoleId, PermissionId)
            SELECT r.Id, tp.PermissionId
            FROM auth.Roles r
            JOIN auth.Roles t            ON t.Code = r.Code AND t.IsTemplate = 1 AND t.InstitutionId IS NULL
            JOIN auth.RolePermissions tp ON tp.RoleId = t.Id
            WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';
            """,
            new { alpha = database.AlphaInstitutionId });

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

    /// <summary>Noon in Colombo on <paramref name="today"/>.</summary>
    private static FixedTimeProvider ClockAt(DateOnly today) => new(SriLankaTime.ToUtc(today, new TimeOnly(12, 0)));

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<(Guid Id, string Phone)> DonorAsync(string name = "Co Donor", string language = "en")
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', @name, @phone, 1, @language, 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone, name, language });

        return (id, phone);
    }

    /// <summary>A member of Alpha holding its OWNER role, made once per test.</summary>
    private async Task<Guid> OwnerAsync()
    {
        if (_owner is { } owner)
        {
            return owner;
        }

        var id = Guid.CreateVersion7();
        var phone = "+9478" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'InstitutionStaff', N'Share Owner', @phone, 1, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';
            """,
            new { id, phone, alpha = database.AlphaInstitutionId });

        _owner = id;
        return id;
    }

    /// <summary>A lunch at Alpha, every day, approval needed, a 1-60 day window.</summary>
    private async Task<Guid> SlotAsync()
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @alpha, @name, '11:30', '13:00', 'Shared', 10, 1, 200, 1, 60, 1, 'DonorBrings', @createdBy);
            """,
            new
            {
                publicId,
                alpha = database.AlphaInstitutionId,
                name = "Shared " + Guid.NewGuid().ToString("N")[..8],
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    /// <summary>
    /// <paramref name="donor"/>'s booking of a new slot on <paramref name="daysAhead"/> days after
    /// <paramref name="today"/>, made through the engine as if on <paramref name="today"/>.
    /// </summary>
    private async Task<string> BookingAsync(Guid donor, int daysAhead = 5, DateOnly? today = null)
    {
        var day = today ?? Today;
        var slot = await SlotAsync();
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var engine = new BookMealHandler(context, new NoLocks(), new SqlSlotInventory(context), new SqlNumberSeries(context), ClockAt(day));
        var result = await engine.BookAsync(donor, new BookMealCommand(slot, day.AddDays(daysAhead), 10, FulfilmentMode.DonorBrings, "Amma", "Bring the big pot", false, null, null));
        Assert.True(result.Succeeded, result.Error);
        return result.BookingNo!;
    }

    private static ContributorHandler InvitationsFor(SethikaDbContext context, DateOnly? today = null) =>
        new(context, new SqlBookingRowLocks(context), today is { } day ? ClockAt(day) : TimeProvider.System);

    /// <summary>An invitation made through the handler as the donor; its id and its link's token.</summary>
    private async Task<(Guid Id, string Token)> InviteAsync(Guid donor, string bookingNo, string name = "Nangi", DateOnly? today = null)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var result = await InvitationsFor(context, today).InviteAsync(donor, bookingNo, new InviteContributorCommand(name, "Brings dessert"));
        Assert.True(result.Succeeded, result.Error);
        return (result.Contributor!.Value, result.Token!);
    }

    private HttpClient ClientFor(Guid? userId, int? institutionId = null)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.9.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        if (userId is { } id)
        {
            var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(id, institutionId, IsPlatform: false));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        }

        return client;
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} {text}");
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    private static string CodeOf(JsonElement body) => body.GetProperty("code").GetString()!;

    private sealed record Row(string Status, Guid? UserId, DateTime? JoinedAt, DateTime? DeclinedAt);

    /// <summary>
    /// Waits until <paramref name="count"/> requests in this database wait on a lock, so a race is
    /// really run: a fixed delay would also pass while a request was merely slow to start.
    /// </summary>
    private async Task WaitForBlockedAsync(int count)
    {
        await using var watcher = new SqlConnection(database.ConnectionString);
        await watcher.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await watcher.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id <> 0;") < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Fewer than {count} requests ever waited on the lock.");
            await Task.Delay(50);
        }
    }

    private async Task<Row> RowAsync(Guid contributor)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<Row>(
            "SELECT Status, UserId, JoinedAt, DeclinedAt FROM booking.BookingContributors WHERE PublicId = @contributor;",
            new { contributor });
    }

    // ------------------------------------------------------------------ the whole way through

    [Fact]
    public async Task TheDonorInvites_TheLinkShowsTheSummary_SomeoneJoins_AndEverySideSeesIt()
    {
        var (donor, _) = await DonorAsync("Kamala Perera");
        var (sister, _) = await DonorAsync("Nirmala Perera");
        var bookingNo = await BookingAsync(donor);

        // The donor invites: a link's secret once, and only its hash stored.
        var invited = await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/contributors", HttpStatusCode.Created,
            new { name = "  Nangi  ", note = "Brings dessert" });
        var token = invited.GetProperty("token").GetString()!;
        var contributor = invited.GetProperty("contributor").GetGuid();
        Assert.Equal(ShareToken.Length, token.Length);
        Assert.Equal(ShareToken.HashOf(token), await ScalarAsync<string>(
            "SELECT InviteTokenHash FROM booking.BookingContributors WHERE PublicId = @contributor;", new { contributor }));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.BookingContributors WHERE InviteTokenHash = @token;", new { token }));

        var invitations = await SendAsync(ClientFor(donor), HttpMethod.Get, $"/api/bookings/{bookingNo}/contributors", HttpStatusCode.OK);
        var invitation = Assert.Single(invitations.EnumerateArray());
        Assert.Equal((contributor, "Nangi", "Brings dessert", "Invited"), (
            invitation.GetProperty("publicId").GetGuid(), invitation.GetProperty("name").GetString(),
            invitation.GetProperty("note").GetString(), invitation.GetProperty("status").GetString()));

        // Anyone with the link sees the summary, and nothing more (H17): no number, phone, headcount or notes.
        var summary = await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{token}", HttpStatusCode.OK);
        Assert.Equal(
            ["booking", "date", "donorName", "inMemoryOf", "institution", "institutionName", "institutionNameSi", "institutionNameTa",
             "invitation", "mealName", "mealNameSi", "mealNameTa", "startTime"],
            summary.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("Kamala Perera", "Amma", "11:30", "Invited", "Open"), (
            summary.GetProperty("donorName").GetString(), summary.GetProperty("inMemoryOf").GetString(),
            summary.GetProperty("startTime").GetString(), summary.GetProperty("invitation").GetString(),
            summary.GetProperty("booking").GetString()));
        Assert.Equal(Today.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), summary.GetProperty("date").GetString());
        Assert.DoesNotContain(bookingNo, summary.GetRawText(), StringComparison.Ordinal);

        // The sister joins.
        await SendAsync(ClientFor(sister), HttpMethod.Post, $"/api/share/{token}/join", HttpStatusCode.NoContent);
        var row = await RowAsync(contributor);
        Assert.Equal(("Joined", (Guid?)sister), (row.Status, row.UserId));
        Assert.NotNull(row.JoinedAt);
        Assert.Null(row.DeclinedAt);

        Assert.Equal("Joined", (await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{token}", HttpStatusCode.OK)).GetProperty("invitation").GetString());
        Assert.Equal("Joined", (await SendAsync(ClientFor(donor), HttpMethod.Get, $"/api/bookings/{bookingNo}/contributors", HttpStatusCode.OK))[0].GetProperty("status").GetString());

        // Shared with me: the sister sees the booking she joined; the donor's own list does not hold it.
        var shared = Assert.Single((await SendAsync(ClientFor(sister), HttpMethod.Get, "/api/bookings/shared", HttpStatusCode.OK)).EnumerateArray());
        Assert.Equal((bookingNo, "Requested", "Kamala Perera", (short)10), (
            shared.GetProperty("bookingNo").GetString(), shared.GetProperty("status").GetString(),
            shared.GetProperty("donorName").GetString(), shared.GetProperty("headcount").GetInt16()));
        Assert.Empty((await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/bookings/shared", HttpStatusCode.OK)).EnumerateArray());

        // The institution sees who joined, names only; an invitation still waiting is not shown.
        await InviteAsync(donor, bookingNo, "Still waiting");
        var owner = await OwnerAsync();
        var names = await SendAsync(ClientFor(owner, database.AlphaInstitutionId), HttpMethod.Get, $"/api/institution/bookings/{bookingNo}/shared-with", HttpStatusCode.OK);
        var name = Assert.Single(names.EnumerateArray());
        Assert.Equal(["joinedAt", "name"], name.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Nangi", name.GetProperty("name").GetString());

        // Joined is final: joining again, or declining, is refused; the donor cannot withdraw it.
        Assert.Equal(ShareErrors.InvitationAnswered, CodeOf(await SendAsync(ClientFor(sister), HttpMethod.Post, $"/api/share/{token}/join", HttpStatusCode.Conflict)));
        Assert.Equal(ShareErrors.InvitationAnswered, CodeOf(await SendAsync(ClientFor(sister), HttpMethod.Post, $"/api/share/{token}/decline", HttpStatusCode.Conflict)));
        Assert.Equal(ContributorResult.InvitationAnswered, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Delete,
            $"/api/bookings/{bookingNo}/contributors/{contributor}", HttpStatusCode.Conflict)));
    }

    [Fact]
    public async Task ADeclineIsFinal_AndAWithdrawnLinkOpensNothing()
    {
        var (donor, _) = await DonorAsync();
        var (friend, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        var (declined, declinedToken) = await InviteAsync(donor, bookingNo, "Friend");
        var (withdrawn, withdrawnToken) = await InviteAsync(donor, bookingNo, "Neighbour");

        await SendAsync(ClientFor(friend), HttpMethod.Post, $"/api/share/{declinedToken}/decline", HttpStatusCode.NoContent);
        var row = await RowAsync(declined);
        Assert.Equal(("Declined", (Guid?)friend, (DateTime?)null), (row.Status, row.UserId, row.JoinedAt));
        Assert.NotNull(row.DeclinedAt);
        Assert.Equal(ShareErrors.InvitationAnswered, CodeOf(await SendAsync(ClientFor(friend), HttpMethod.Post, $"/api/share/{declinedToken}/join", HttpStatusCode.Conflict)));
        Assert.Equal("Declined", (await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{declinedToken}", HttpStatusCode.OK)).GetProperty("invitation").GetString());
        Assert.Empty((await SendAsync(ClientFor(friend), HttpMethod.Get, "/api/bookings/shared", HttpStatusCode.OK)).EnumerateArray());

        // Withdrawn while nobody had answered: gone, and its link with it.
        await SendAsync(ClientFor(donor), HttpMethod.Delete, $"/api/bookings/{bookingNo}/contributors/{withdrawn}", HttpStatusCode.NoContent);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.BookingContributors WHERE PublicId = @withdrawn;", new { withdrawn }));
        Assert.Equal(ShareErrors.LinkNotFound, CodeOf(await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{withdrawnToken}", HttpStatusCode.NotFound)));
        Assert.Equal(ShareErrors.LinkNotFound, CodeOf(await SendAsync(ClientFor(friend), HttpMethod.Post, $"/api/share/{withdrawnToken}/join", HttpStatusCode.NotFound)));
        Assert.Equal(ContributorResult.InvitationNotFound, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Delete,
            $"/api/bookings/{bookingNo}/contributors/{withdrawn}", HttpStatusCode.NotFound)));
        Assert.Equal(ContributorResult.InvitationAnswered, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Delete,
            $"/api/bookings/{bookingNo}/contributors/{declined}", HttpStatusCode.Conflict)));
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task ALinkThatDoesNotExist_TheDonorsOwn_AndASecondJoiningOfOneBooking_AreRefused()
    {
        var (donor, _) = await DonorAsync();
        var (brother, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        var (_, first) = await InviteAsync(donor, bookingNo, "Aiya");
        var (second, secondToken) = await InviteAsync(donor, bookingNo, "Aiya again");

        // Well-formed but unknown, malformed, and far too long: one answer, and the database is asked only for the first.
        foreach (var token in new[] { ShareToken.New().Token, "not-a-token", new string('A', 200) })
        {
            Assert.Equal(ShareErrors.LinkNotFound, CodeOf(await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{token}", HttpStatusCode.NotFound)));
            Assert.Equal(ShareErrors.LinkNotFound, CodeOf(await SendAsync(ClientFor(brother), HttpMethod.Post, $"/api/share/{token}/join", HttpStatusCode.NotFound)));
        }

        // The donor opening their own link.
        Assert.Equal(ShareErrors.OwnBooking, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/share/{first}/join", HttpStatusCode.Conflict)));
        Assert.Equal(ShareErrors.OwnBooking, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/share/{first}/decline", HttpStatusCode.Conflict)));

        // One person joins a booking once, whichever of its links they open.
        await SendAsync(ClientFor(brother), HttpMethod.Post, $"/api/share/{first}/join", HttpStatusCode.NoContent);
        Assert.Equal(ShareErrors.AlreadyJoined, CodeOf(await SendAsync(ClientFor(brother), HttpMethod.Post, $"/api/share/{secondToken}/join", HttpStatusCode.Conflict)));
        Assert.Equal("Invited", (await RowAsync(second)).Status);

        // Answering needs an account: anonymous is 401, an institution session 403.
        await SendAsync(ClientFor(null), HttpMethod.Post, $"/api/share/{secondToken}/join", HttpStatusCode.Unauthorized);
        await SendAsync(ClientFor(await OwnerAsync(), database.AlphaInstitutionId), HttpMethod.Post, $"/api/share/{secondToken}/join", HttpStatusCode.Forbidden);
        Assert.Equal("Invited", (await RowAsync(second)).Status);
    }

    [Fact]
    public async Task ACancelledRejectedOrPastBooking_ShowsItsState_AndCannotBeSharedOrJoined()
    {
        var (donor, _) = await DonorAsync();
        var (friend, _) = await DonorAsync();

        var cancelled = await BookingAsync(donor);
        var (_, cancelledToken) = await InviteAsync(donor, cancelled);
        await ExecuteAsync(
            "UPDATE booking.Bookings SET Status = 'Cancelled', CancelledAt = SYSUTCDATETIME(), CancelledBy = @donor WHERE BookingNo = @cancelled;",
            new { donor, cancelled });

        var rejected = await BookingAsync(donor);
        var (_, rejectedToken) = await InviteAsync(donor, rejected);
        await ExecuteAsync(
            "UPDATE booking.Bookings SET Status = 'Rejected', DecidedAt = SYSUTCDATETIME(), DecidedBy = @owner, RejectReason = N'Full' WHERE BookingNo = @rejected;",
            new { owner = SethikaTestDatabase.AlphaUserId, rejected });

        // Booked and shared ten days ago, for the day before yesterday.
        var tenDaysAgo = Today.AddDays(-10);
        var past = await BookingAsync(donor, daysAhead: 8, today: tenDaysAgo);
        var (_, pastToken) = await InviteAsync(donor, past, today: tenDaysAgo);

        // Today's booking is still open (its day has not gone).
        var todays = await BookingAsync(donor, daysAhead: 1, today: Today.AddDays(-1));
        var (_, todaysToken) = await InviteAsync(donor, todays);
        Assert.Equal("Open", (await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{todaysToken}", HttpStatusCode.OK)).GetProperty("booking").GetString());

        foreach (var (bookingNo, token, state) in new[] { (cancelled, cancelledToken, "Cancelled"), (rejected, rejectedToken, "Rejected"), (past, pastToken, "Past") })
        {
            Assert.Equal(state, (await SendAsync(ClientFor(null), HttpMethod.Get, $"/api/share/{token}", HttpStatusCode.OK)).GetProperty("booking").GetString());
            Assert.Equal(ShareErrors.BookingClosed, CodeOf(await SendAsync(ClientFor(friend), HttpMethod.Post, $"/api/share/{token}/join", HttpStatusCode.Conflict)));
            Assert.Equal(ContributorResult.BookingClosed, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/contributors",
                HttpStatusCode.Conflict, new { name = "Late" })));
        }
    }

    [Fact]
    public async Task AnInvitationNeedsAName_FitsItsColumns_IsTheDonorsOwnBooking_AndStopsAtTen()
    {
        var (donor, _) = await DonorAsync();
        var (other, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        var path = $"/api/bookings/{bookingNo}/contributors";

        var invalid = await SendAsync(ClientFor(donor), HttpMethod.Post, path, HttpStatusCode.BadRequest,
            new { name = "   ", note = new string('n', BookingContributor.MaxNote + 1) });
        Assert.Equal(ContributorResult.InvalidDetails, CodeOf(invalid));
        Assert.Equal(["name", "note"], invalid.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["name"], (await SendAsync(ClientFor(donor), HttpMethod.Post, path, HttpStatusCode.BadRequest,
            new { name = new string('x', BookingContributor.MaxName + 1) })).GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        await SendAsync(ClientFor(donor), HttpMethod.Post, path, HttpStatusCode.Created,
            new { name = new string('x', BookingContributor.MaxName), note = new string('n', BookingContributor.MaxNote) });

        // Another donor's booking, and one that does not exist, are not found; a list of either too.
        Assert.Equal(ContributorResult.BookingNotFound, CodeOf(await SendAsync(ClientFor(other), HttpMethod.Post, path, HttpStatusCode.NotFound, new { name = "Me" })));
        Assert.Equal(ContributorResult.BookingNotFound, CodeOf(await SendAsync(ClientFor(other), HttpMethod.Get, path, HttpStatusCode.NotFound)));
        Assert.Equal(ContributorResult.BookingNotFound, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Post, "/api/bookings/BK-1900-000001/contributors", HttpStatusCode.NotFound, new { name = "Me" })));

        // Ten in all, answered or not; the eleventh is refused.
        for (var i = 2; i <= BookingContributor.MaxPerBooking; i++)
        {
            await SendAsync(ClientFor(donor), HttpMethod.Post, path, HttpStatusCode.Created, new { name = $"Guest {i}" });
        }

        Assert.Equal(ContributorResult.TooManyInvitations, CodeOf(await SendAsync(ClientFor(donor), HttpMethod.Post, path, HttpStatusCode.Conflict, new { name = "Eleventh" })));
        Assert.Equal(BookingContributor.MaxPerBooking, (await SendAsync(ClientFor(donor), HttpMethod.Get, path, HttpStatusCode.OK)).GetArrayLength());

        // My bookings' permissions: an institution session is refused.
        await SendAsync(ClientFor(donor, database.AlphaInstitutionId), HttpMethod.Get, path, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InvitationsMadeAtOnce_StopAtTenAll_TheSame()
    {
        var (donor, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        for (var i = 1; i <= BookingContributor.MaxPerBooking - 2; i++)
        {
            await InviteAsync(donor, bookingNo, $"Guest {i}");
        }

        // Another session holds the booking's row lock, as an invitation in flight would; four more
        // are made meanwhile and wait for it. Released, they go one at a time, so two get the last places.
        await using var holder = await PlatformConnectionAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT COUNT(*) FROM booking.Bookings WITH (UPDLOCK, ROWLOCK) WHERE BookingNo = @bookingNo;", new { bookingNo }, transaction);

        var attempts = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
            return await InvitationsFor(context).InviteAsync(donor, bookingNo, new InviteContributorCommand($"Racer {i}", null));
        })).ToList();

        await WaitForBlockedAsync(4);
        Assert.All(attempts, a => Assert.False(a.IsCompleted));
        await transaction.CommitAsync();

        var results = await Task.WhenAll(attempts);
        Assert.Equal(2, results.Count(r => r.Succeeded));
        Assert.Equal(2, results.Count(r => r.Error == ContributorResult.TooManyInvitations));
        Assert.Equal(BookingContributor.MaxPerBooking, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.BookingContributors c JOIN booking.Bookings b ON b.Id = c.BookingId WHERE b.BookingNo = @bookingNo;", new { bookingNo }));
    }

    [Fact]
    public async Task TwoPeopleAnsweringOneLinkAtOnce_TheFirstHasIt()
    {
        var (donor, _) = await DonorAsync();
        var (first, _) = await DonorAsync();
        var (second, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        var (contributor, token) = await InviteAsync(donor, bookingNo);

        // The first person's joining is in flight - written, not yet committed - when the second
        // answers. The second read the link as still waiting, so only the answer's own condition can
        // stop it from writing over the first.
        await using var holder = await PlatformConnectionAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            """
            UPDATE booking.BookingContributors SET UserId = @first, Status = 'Joined', JoinedAt = SYSUTCDATETIME()
            WHERE PublicId = @contributor;
            """,
            new { first, contributor }, transaction);

        var racer = Task.Run(() => SendAsync(ClientFor(second), HttpMethod.Post, $"/api/share/{token}/decline", HttpStatusCode.Conflict));
        await WaitForBlockedAsync(1);
        Assert.False(racer.IsCompleted);
        await transaction.CommitAsync();

        Assert.Equal(ShareErrors.InvitationAnswered, CodeOf(await racer));
        var row = await RowAsync(contributor);
        Assert.Equal(("Joined", (Guid?)first, (DateTime?)null), (row.Status, row.UserId, row.DeclinedAt));
    }

    // ------------------------------------------------------------------ Row-Level Security

    [Fact]
    public async Task EachSessionSeesTheRowsItShould_AndCannotWriteAnothersInvitation()
    {
        var (donor, _) = await DonorAsync();
        var (joiner, _) = await DonorAsync();
        var (stranger, _) = await DonorAsync();
        var bookingNo = await BookingAsync(donor);
        var (_, token) = await InviteAsync(donor, bookingNo, "Joins");
        await InviteAsync(donor, bookingNo, "Waits");
        await SendAsync(ClientFor(joiner), HttpMethod.Post, $"/api/share/{token}/join", HttpStatusCode.NoContent);

        // By the booking's id, which the platform reads: Bookings' own RLS hides it from all but the
        // donor and the institution, so a lookup by number would hide the rows from the rest for the
        // wrong reason.
        var bookingId = await ScalarAsync<long>("SELECT Id FROM booking.Bookings WHERE BookingNo = @bookingNo;", new { bookingNo });
        async Task<string[]> NamesSeenBy(ICurrentUserContext user)
        {
            await using var connection = await ConnectionsFor(user).OpenConnectionAsync();
            return [.. await connection.QueryAsync<string>("SELECT Name FROM booking.BookingContributors WHERE BookingId = @bookingId ORDER BY Id;", new { bookingId })];
        }

        Assert.Equal(["Joins", "Waits"], await NamesSeenBy(TestCurrentUserContext.Platform()));
        Assert.Equal(["Joins", "Waits"], await NamesSeenBy(TestCurrentUserContext.ForUser(donor)));
        Assert.Equal(["Joins", "Waits"], await NamesSeenBy(TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId)));
        Assert.Equal(["Joins"], await NamesSeenBy(TestCurrentUserContext.ForUser(joiner)));
        Assert.Empty(await NamesSeenBy(TestCurrentUserContext.ForUser(stranger)));
        Assert.Empty(await NamesSeenBy(TestCurrentUserContext.ForInstitution(database.BetaInstitutionId)));
        Assert.Empty(await NamesSeenBy(TestCurrentUserContext.Anonymous()));

        // The same through the handlers' reads: another donor's and another institution's are not found.
        Assert.Null(await new SharingQueries(ConnectionsFor(TestCurrentUserContext.ForUser(stranger))).ListInvitationsAsync(stranger, bookingNo));
        Assert.Null(await new SharingQueries(ConnectionsFor(TestCurrentUserContext.ForInstitution(database.BetaInstitutionId)))
            .ListSharedWithAsync(database.BetaInstitutionId, bookingNo));

        // A stranger cannot write an invitation in the donor's name onto the donor's booking.
        await using (var connection = await ConnectionsFor(TestCurrentUserContext.ForUser(stranger)).OpenConnectionAsync())
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                """
                INSERT booking.BookingContributors (BookingId, InstitutionId, Name, InviteTokenHash, InvitedBy, PublicId)
                VALUES (@bookingId, @alpha, N'Forged', @hash, @donor, NEWID());
                """,
                new { bookingId, alpha = database.AlphaInstitutionId, hash = ShareToken.New().Hash, donor }));
            Assert.Contains("block predicate", refused.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Nor can the joiner hand their joining to someone else.
        await using (var connection = await ConnectionsFor(TestCurrentUserContext.ForUser(joiner)).OpenConnectionAsync())
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "UPDATE booking.BookingContributors SET UserId = @stranger WHERE BookingId = @bookingId AND UserId = @joiner;",
                new { stranger, bookingId, joiner }));
            Assert.Contains("block predicate", refused.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ the reminder

    [Fact]
    public async Task TheDayBefore_EachJoinedCoSponsorIsRemindedOnce_LikeTheDonor_AndNobodyElse()
    {
        var (donor, donorPhone) = await DonorAsync();
        var (joined, joinedPhone) = await DonorAsync(language: "si");
        var (declined, declinedPhone) = await DonorAsync();
        var bookingNo = await BookingAsync(donor, daysAhead: 1, today: Today);
        var (_, joinToken) = await InviteAsync(donor, bookingNo, "Joins");
        var (_, declineToken) = await InviteAsync(donor, bookingNo, "Declines");
        await InviteAsync(donor, bookingNo, "Never answers");
        await SendAsync(ClientFor(joined), HttpMethod.Post, $"/api/share/{joinToken}/join", HttpStatusCode.NoContent);
        await SendAsync(ClientFor(declined), HttpMethod.Post, $"/api/share/{declineToken}/decline", HttpStatusCode.NoContent);

        // A requested booking is no meal to remind anyone of; approved, it is.
        var connections = ConnectionsFor(TestCurrentUserContext.Platform());
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var reminders = new BookingReminders(
            new BookingNotificationQueries(connections), new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance), TimeProvider.System);

        await reminders.SendForTomorrowAsync();
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM notify.Notifications WHERE Recipient IN @phones;", new { phones = new[] { donorPhone, joinedPhone } }));

        await ExecuteAsync(
            "UPDATE booking.Bookings SET Status = 'Approved', DecidedAt = SYSUTCDATETIME(), DecidedBy = @owner WHERE BookingNo = @bookingNo;",
            new { owner = SethikaTestDatabase.AlphaUserId, bookingNo });

        var run = await reminders.SendForTomorrowAsync();
        await reminders.SendForTomorrowAsync();
        Assert.True(run.Contributors >= 1);

        async Task<List<(string Body, string DedupKey)>> SentTo(string phone)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            return [.. await connection.QueryAsync<(string, string)>(
                "SELECT Body, DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;", new { phone })];
        }

        var toDonor = Assert.Single(await SentTo(donorPhone));
        Assert.Equal(BookingReminders.DedupKey(bookingNo), toDonor.DedupKey);

        // The co-sponsor's own key, in their own language, with the booking's number and day.
        var toJoined = Assert.Single(await SentTo(joinedPhone));
        Assert.Equal(BookingReminders.DedupKey(bookingNo, joined), toJoined.DedupKey);
        Assert.Contains(bookingNo, toJoined.Body, StringComparison.Ordinal);
        Assert.Contains("මතක් කිරීමක්", toJoined.Body, StringComparison.Ordinal);

        Assert.Empty(await SentTo(declinedPhone));
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.Hubs;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Live;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 07, C.3: live slot updates. Real hosts, a real SignalR client, the real outbox and Redis: a
/// calendar watching an institution hears each kind of change to its days, one watching another
/// hears nothing, and a change made on one host reaches a calendar connected to another through the
/// Redis backplane - while a host on another channel prefix hears nothing of it.
/// </summary>
/// <remarks>
/// Every host here shares a channel prefix of this run's own, so neither the dev API nor another run
/// on the same Redis can hear these hints or send any. The outbox runs as its job runs it, as the
/// platform, through <see cref="OutboxJob"/>.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class LiveAvailabilityTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "live-availability-test-signing-key-at-least-32-bytes";
    private const string AlphaSlug = "alpha-temple";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly string _prefix = "sethika-test-" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private readonly List<IAsyncDisposable> _disposables = [];
    private string _betaSlug = null!;
    private Guid? _owner;

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;");

        // Alpha's OWNER role, cloned from the template as onboarding's approval clones it.
        await connection.ExecuteAsync(
            """
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
        _betaSlug = await connection.ExecuteScalarAsync<string>("SELECT Slug FROM org.Institutions WHERE Id = @beta;", new { beta = database.BetaInstitutionId }) ?? throw new InvalidOperationException("No Beta.");
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        await database.SeedAsync();
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM notify.Notifications WHERE Recipient IN @phones;
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { phones = _phones, users = _users });
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

    private sealed class RecordingWake : IOutboxWake
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Wake() => Interlocked.Increment(ref _count);
    }

    private WebApplicationFactory<Program> Host(string? prefix = null, IOutboxWake? wake = null)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("SignalR:ChannelPrefix", prefix ?? _prefix);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new LoopbackProxyStartupFilter());
                if (wake is not null)
                {
                    services.AddSingleton(wake);
                }
            });
        });

        _disposables.Add(host);
        return host;
    }

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    /// <summary>A calendar: a SignalR client on <paramref name="host"/>, watching <paramref name="slug"/>, and what it hears.</summary>
    private async Task<(HubConnection Connection, Channel<AvailabilityChangedMessage> Heard)> CalendarAsync(WebApplicationFactory<Program> host, string slug)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, AvailabilityHub.Path.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        // Heard as the web's calendar hears it (web/lib/bookings/live.ts): by the message's name and its
        // JSON fields, typed out here, so renaming either on the server breaks this as it would the web.
        var heard = Channel.CreateUnbounded<AvailabilityChangedMessage>();
        connection.On<JsonElement>("AvailabilityChanged", message => heard.Writer.TryWrite(new AvailabilityChangedMessage(
            message.GetProperty("slug").GetString()!,
            message.GetProperty("from").GetString(),
            message.GetProperty("to").GetString())));
        _disposables.Add(connection);

        await connection.StartAsync();
        await connection.InvokeAsync("Watch", slug);
        return (connection, heard);
    }

    private static async Task<AvailabilityChangedMessage> NextAsync(Channel<AvailabilityChangedMessage> heard)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await heard.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>The outbox's run, as its job runs it: in a platform scope of <paramref name="host"/>.</summary>
    private static Task RunOutboxAsync(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<OutboxJob>().RunAsync(CancellationToken.None);

    private async Task<Guid> DonorAsync()
    {
        var id = Guid.CreateVersion7();
        var phone = "+9478" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Live Donor', @phone, 1, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone });

        return id;
    }

    /// <summary>A lunch at Alpha, every day, approval needed, a 1-60 day window.</summary>
    private async Task<Guid> SlotAsync()
    {
        var publicId = Guid.CreateVersion7();
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, AllowedFulfilment, CreatedBy)
            VALUES (@publicId, @alpha, @name, '11:30', '13:00', 'Shared', 10, 1, 200, 1, 60, 1, 'DonorBrings', @createdBy);
            """,
            new { publicId, alpha = database.AlphaInstitutionId, name = "Live " + Guid.NewGuid().ToString("N")[..8], createdBy = SethikaTestDatabase.AlphaUserId });

        return publicId;
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> host, Guid userId, int? institutionId = null)
    {
        var tokens = host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(userId, institutionId, IsPlatform: false));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.9.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        return client;
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

        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'InstitutionStaff', N'Live Owner', @phone, 1, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @alpha AND r.Code = 'OWNER';
            """,
            new { id, phone, alpha = database.AlphaInstitutionId });

        _owner = id;
        return id;
    }

    private async Task<HttpClient> OwnerOfAsync(WebApplicationFactory<Program> host) => ClientFor(host, await OwnerAsync(), database.AlphaInstitutionId);

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

    private static object Booking(Guid slot, DateOnly date) =>
        new { mealSlot = slot, date = Iso(date), headcount = 5, fulfilment = "DonorBrings" };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ one host

    [Fact]
    public async Task ACalendarHearsEachChangeToItsInstitutionsDays_AndOneWatchingAnotherHearsNothing()
    {
        var host = Host();
        var (_, alpha) = await CalendarAsync(host, AlphaSlug);
        var (_, beta) = await CalendarAsync(host, _betaSlug);
        var donor = await DonorAsync();
        var slot = await SlotAsync();
        var day = Today.AddDays(9);

        // A booking takes a place.
        var bookingNo = (await SendAsync(ClientFor(host, donor), HttpMethod.Post, "/api/bookings", HttpStatusCode.Created, Booking(slot, day)))
            .GetProperty("bookingNo").GetString();
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day), Iso(day)), await NextAsync(alpha));

        // Approving it changes no place, but a colleague's calendar still shows it waiting: it hears the day (QA DEF-008).
        await SendAsync(await OwnerOfAsync(host), HttpMethod.Post, $"/api/institution/bookings/{bookingNo}/approve", HttpStatusCode.OK);
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day), Iso(day)), await NextAsync(alpha));

        // Rejecting another gives its place back.
        var other = await DonorAsync();
        var rejected = (await SendAsync(ClientFor(host, other), HttpMethod.Post, "/api/bookings", HttpStatusCode.Created, Booking(slot, day.AddDays(1))))
            .GetProperty("bookingNo").GetString();
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day.AddDays(1)), Iso(day.AddDays(1))), await NextAsync(alpha));

        await SendAsync(await OwnerOfAsync(host), HttpMethod.Post, $"/api/institution/bookings/{rejected}/reject", HttpStatusCode.OK, new { reason = "Full that day" });
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day.AddDays(1)), Iso(day.AddDays(1))), await NextAsync(alpha));

        // The donor cancels theirs.
        await SendAsync(ClientFor(host, donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel", HttpStatusCode.OK);
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day), Iso(day)), await NextAsync(alpha));

        // Days blocked, and opened again: the whole range.
        var block = (await SendAsync(await OwnerOfAsync(host), HttpMethod.Post, "/api/institution/blocked-dates", HttpStatusCode.Created,
            new { dateFrom = Iso(day.AddDays(3)), dateTo = Iso(day.AddDays(5)), reason = "Perahera" })).GetProperty("id").GetInt32();
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day.AddDays(3)), Iso(day.AddDays(5))), await NextAsync(alpha));

        await SendAsync(await OwnerOfAsync(host), HttpMethod.Delete, $"/api/institution/blocked-dates/{block}", HttpStatusCode.NoContent);
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day.AddDays(3)), Iso(day.AddDays(5))), await NextAsync(alpha));

        // A slot switched off may change any day.
        await SendAsync(await OwnerOfAsync(host), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate", HttpStatusCode.OK);
        await RunOutboxAsync(host);
        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, null, null), await NextAsync(alpha));

        // Seven changes, seven hints, the approval's included; Beta's calendar heard none of them.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(alpha.Reader.TryRead(out var extra), $"An extra hint: {extra}");
        Assert.False(beta.Reader.TryRead(out var stray), $"Beta heard Alpha's change: {stray}");
    }

    [Fact]
    public async Task ACalendarThatStopsWatchingHearsNoMore()
    {
        var host = Host();
        var (connection, alpha) = await CalendarAsync(host, AlphaSlug);
        var slot = await SlotAsync();

        await connection.InvokeAsync("Unwatch", AlphaSlug);
        await SendAsync(ClientFor(host, await DonorAsync()), HttpMethod.Post, "/api/bookings", HttpStatusCode.Created, Booking(slot, Today.AddDays(4)));
        await RunOutboxAsync(host);

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(alpha.Reader.TryRead(out _));
    }

    [Fact]
    public async Task TheHubTakesASlugAndAFewOfThemPerConnection_NoMore()
    {
        var host = Host();
        var (connection, _) = await CalendarAsync(host, AlphaSlug);

        Assert.Contains("invalid_slug", (await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Watch", "  "))).Message, StringComparison.Ordinal);
        Assert.Contains("invalid_slug", (await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Watch", new string('a', 151)))).Message, StringComparison.Ordinal);

        // Watching the same one again is not another; the fourth is refused, until one is let go.
        await connection.InvokeAsync("Watch", AlphaSlug);
        await connection.InvokeAsync("Watch", _betaSlug);
        await connection.InvokeAsync("Watch", "a-third-place");
        Assert.Contains("too_many_watched", (await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Watch", "a-fourth-place"))).Message, StringComparison.Ordinal);
        await connection.InvokeAsync("Watch", AlphaSlug);

        await connection.InvokeAsync("Unwatch", "a-third-place");
        await connection.InvokeAsync("Watch", "a-fourth-place");
    }

    [Fact]
    public async Task ASuccessfulChangeWakesTheOutbox_ARefusalDoesNot()
    {
        var wake = new RecordingWake();
        var host = Host(wake: wake);
        var donor = await DonorAsync();
        var slot = await SlotAsync();

        var bookingNo = (await SendAsync(ClientFor(host, donor), HttpMethod.Post, "/api/bookings", HttpStatusCode.Created, Booking(slot, Today.AddDays(6))))
            .GetProperty("bookingNo").GetString();
        Assert.Equal(1, wake.Count);

        // Refused: the same day again, then a day out of the window.
        await SendAsync(ClientFor(host, donor), HttpMethod.Post, "/api/bookings", HttpStatusCode.Conflict, Booking(slot, Today.AddDays(6)));
        await SendAsync(ClientFor(host, donor), HttpMethod.Post, "/api/bookings", HttpStatusCode.Conflict, Booking(slot, Today.AddDays(90)));
        Assert.Equal(1, wake.Count);

        await SendAsync(ClientFor(host, donor), HttpMethod.Post, $"/api/bookings/{bookingNo}/cancel", HttpStatusCode.OK);
        await SendAsync(await OwnerOfAsync(host), HttpMethod.Post, $"/api/institution/meal-slots/{slot}/deactivate", HttpStatusCode.OK);
        Assert.Equal(3, wake.Count);
    }

    // ------------------------------------------------------------------ two hosts

    [Fact]
    public async Task TwoHostsOnOneRedis_AChangeOnOneReachesACalendarOnTheOther_ButNotAHostOnAnotherPrefix()
    {
        var hostA = Host();
        var hostB = Host();
        var elsewhere = Host(prefix: _prefix + "-elsewhere");

        var (_, onB) = await CalendarAsync(hostB, AlphaSlug);
        var (_, onElsewhere) = await CalendarAsync(elsewhere, AlphaSlug);
        var slot = await SlotAsync();
        var day = Today.AddDays(12);

        await SendAsync(ClientFor(hostA, await DonorAsync()), HttpMethod.Post, "/api/bookings", HttpStatusCode.Created, Booking(slot, day));
        await RunOutboxAsync(hostA);

        Assert.Equal(new AvailabilityChangedMessage(AlphaSlug, Iso(day), Iso(day)), await NextAsync(onB));

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(onElsewhere.Reader.TryRead(out var stray), $"A host on another prefix heard it: {stray}");
    }

    [Fact]
    public void TheHostSendsTheHintThroughItsHub_InTheOutboxsOneHandlerForIt()
    {
        var host = Host();
        using var scope = host.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToList();
        Assert.IsType<AvailabilityChangedHandler>(Assert.Single(handlers, h => h.EventTypes.Contains(AvailabilityChange.Event)));
        Assert.Equal("HubAvailabilityNotifier", scope.ServiceProvider.GetRequiredService<IAvailabilityNotifier>().GetType().Name);
    }
}

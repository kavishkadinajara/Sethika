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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Notifications.Devices;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Xunit;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// <c>PUT /api/me/devices</c> and <c>POST /api/me/devices/remove</c> (plan F, F31) over HTTP against
/// real SQL Server and Redis, in a real host with <c>Push:Provider = Expo</c>, so the wiring from
/// <see cref="IUserTextMessages"/> down to the HTTP client is proved too. Only Expo is stubbed.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class MobileDeviceEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime, IDisposable
{
    private const string SigningKey = "mobile-device-endpoint-test-signing-key-at-least-32-bytes";
    private const string Password = "Dana!2026Sethika";
    private const string Devices = "/api/me/devices";
    private const string Remove = "/api/me/devices/remove";

    private readonly List<string> _phones = [];
    private readonly FakeExpo _expo = new();
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Push:Provider", PushOptions.ExpoProvider);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(nameof(IPushGateway)).ConfigurePrimaryHttpMessageHandler(() => _expo));
        });
    }

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;

                DELETE FROM notify.Notifications WHERE UserId IN (SELECT Id FROM @ids) OR Recipient IN @phones;
                DELETE FROM notify.MobileDevices WHERE UserId IN (SELECT Id FROM @ids);
                UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        await _host.DisposeAsync();
    }

    public void Dispose() => _expo.Dispose();

    // ------------------------------------------------------------------ plumbing

    private HttpClient NewClient(string? accessToken = null)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    private sealed record Session(Guid UserId, string Access, string Refresh);

    /// <summary>A new donor, signed in as the app signs in.</summary>
    private async Task<Session> SignedInDonorAsync()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        var userId = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            OUTPUT inserted.Id
            VALUES (NEWID(), 'Donor', N'Device Test', @phone, 1, @hash, 'en', 'Active');
            """,
            new { phone, hash });

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/mobile/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        return new Session(userId, body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static string NewToken() => $"ExponentPushToken[{Guid.NewGuid():N}]";

    private async Task<HttpResponseMessage> PutDeviceAsync(Session session, object body)
    {
        using var client = NewClient(session.Access);
        return await client.PutAsJsonAsync(Devices, body);
    }

    private async Task RegisterAsync(Session session, string token, string platform = "Android", string? appVersion = "1.0.0")
    {
        using var response = await PutDeviceAsync(session, new { expoPushToken = token, platform, appVersion });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task RemoveAsync(Session session, string token)
    {
        using var client = NewClient(session.Access);
        using var response = await client.PostAsJsonAsync(Remove, new { expoPushToken = token });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record Row(int Id, Guid UserId, string Platform, string? AppVersion, DateTime LastSeenAt, DateTime? DisabledAt, string? DisabledReason);

    private async Task<Row?> RowAsync(string token)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleOrDefaultAsync<Row>(
            """
            SELECT Id, UserId, Platform, AppVersion, LastSeenAt, DisabledAt, DisabledReason
            FROM notify.MobileDevices WHERE ExpoPushToken = @token;
            """,
            new { token });
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync()).GetProperty("code").GetString();

    /// <summary>A text through the host's own <see cref="IUserTextMessages"/>, as a job sends one.</summary>
    private async Task TextAsync(Guid userId, string key)
    {
        await using var scope = _host.Services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<IUserTextMessages>().SendAsync(
            userId,
            "BOOKING_CONFIRMED",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bookingNo"] = "BK-2026-000452",
                ["institution"] = "Alpha Vihara",
                ["date"] = "2026-11-14",
                ["time"] = "11:30",
            },
            key);
    }

    // ------------------------------------------------------------------ registering

    [Fact]
    public async Task ARegisteredDeviceBelongsToTheCaller_AndIsPushedTo()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();

        await RegisterAsync(session, token, "iOS", " 1.2.3 ");

        var row = await RowAsync(token);
        Assert.NotNull(row);
        Assert.Equal((session.UserId, "iOS", "1.2.3"), (row.UserId, row.Platform, row.AppVersion));
        Assert.Null(row.DisabledAt);

        await TextAsync(session.UserId, "BOOKING_CONFIRMED:wired");

        Assert.Equal(token, Assert.Single(_expo.Sent).GetProperty("to").GetString());
    }

    [Fact]
    public async Task RegisteringAgainRefreshesTheSameRow()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(session, token, "Android", "1.0.0");
        var first = await RowAsync(token);

        await RegisterAsync(session, token, "Android", "1.1.0");

        var second = await RowAsync(token);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("1.1.0", second.AppVersion);
        Assert.True(second.LastSeenAt >= first.LastSeenAt);
    }

    [Fact]
    public async Task TheOldExpoPrefixIsAToken()
    {
        await RegisterAsync(await SignedInDonorAsync(), $"ExpoPushToken[{Guid.NewGuid():N}]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("ExponentPushToken[]")]
    [InlineData("ExponentPushToken[abc")]
    [InlineData("xExponentPushToken[abc]")]
    [InlineData("ExponentPushToken[abc]x")]
    [InlineData("ExponentPushToken[a c]")]
    public async Task WhatIsNotAnExpoTokenIsRefused(string? token)
    {
        var session = await SignedInDonorAsync();

        using var response = await PutDeviceAsync(session, new { expoPushToken = token, platform = "Android" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("push_token_invalid", await CodeOf(response));
    }

    [Fact]
    public async Task ATokenLongerThanTheColumnIsRefused_AndOneThatFitsIsNot()
    {
        var session = await SignedInDonorAsync();
        const string shell = "ExponentPushToken[]";

        using var tooLong = await PutDeviceAsync(session, new { expoPushToken = $"ExponentPushToken[{new string('a', 201 - shell.Length)}]", platform = "Android" });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("push_token_invalid", await CodeOf(tooLong));

        var fits = $"ExponentPushToken[{Guid.NewGuid():N}{new string('a', 200 - shell.Length - 32)}]";
        await RegisterAsync(session, fits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("android")]
    [InlineData("Windows")]
    public async Task APlatformOtherThanAndroidOrIosIsRefused(string? platform)
    {
        var session = await SignedInDonorAsync();

        using var response = await PutDeviceAsync(session, new { expoPushToken = NewToken(), platform });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("platform_invalid", await CodeOf(response));
    }

    [Fact]
    public async Task AnAppVersionLongerThanTheColumnIsRefused_AndOneThatFitsIsNot()
    {
        var session = await SignedInDonorAsync();

        using var response = await PutDeviceAsync(session, new { expoPushToken = NewToken(), platform = "Android", appVersion = new string('1', 21) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("app_version_invalid", await CodeOf(response));

        await RegisterAsync(session, NewToken(), appVersion: new string('1', 20));
    }

    [Fact]
    public async Task NoAppVersionIsFine()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();

        await RegisterAsync(session, token, appVersion: null);

        Assert.Null((await RowAsync(token))!.AppVersion);
    }

    [Theory]
    [InlineData("PUT", Devices)]
    [InlineData("POST", Remove)]
    public async Task ThereIsNoAnonymousWayIn(string method, string path)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { expoPushToken = NewToken(), platform = "Android" }),
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OneAccountsChangesAreLimited()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();

        for (var i = 0; i < 60; i++)
        {
            await RemoveAsync(session, token);
        }

        using var client = NewClient(session.Access);
        using var refused = await client.PutAsJsonAsync(Devices, new { expoPushToken = token, platform = "Android" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    // ------------------------------------------------------------------ one token, one owner

    [Fact]
    public async Task AMovedTokenReachesOnlyItsNewOwner()
    {
        var first = await SignedInDonorAsync();
        var second = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(first, token);

        await RegisterAsync(second, token);

        Assert.Equal(second.UserId, (await RowAsync(token))!.UserId);

        await TextAsync(first.UserId, "BOOKING_CONFIRMED:first");
        Assert.Empty(_expo.Sent);

        await TextAsync(second.UserId, "BOOKING_CONFIRMED:second");
        Assert.Single(_expo.Sent);
    }

    /// <summary>
    /// Forces the lost race. A held insert does not reach it: the handler's first UPDATE waits on the
    /// held row and then finds it. So the other registration commits between that UPDATE finding
    /// nothing and this one's insert, which is the only moment the constraint refuses.
    /// </summary>
    [Fact]
    public async Task ARegistrationThatLosesTheInsert_TakesOverTheRowThatWon()
    {
        var winner = await SignedInDonorAsync();
        var loser = await SignedInDonorAsync();
        var token = NewToken();

        var competitor = new CommitsFirst(async () =>
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.ExecuteAsync(
                "INSERT notify.MobileDevices (UserId, ExpoPushToken, Platform) VALUES (@user, @token, 'Android');",
                new { user = winner.UserId, token });
        });

        await using var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.ForUser(loser.UserId)), competitor)
            .Options);

        var error = await new MobileDevicesHandler(context, TimeProvider.System).RegisterAsync(loser.UserId, token, "Android", "1.0.0");

        Assert.Null(error);
        Assert.True(competitor.Ran);
        Assert.Equal(loser.UserId, (await RowAsync(token))!.UserId);
    }

    private sealed class CommitsFirst(Func<Task> competitor) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Ran = true;
            await competitor();
            return result;
        }
    }

    // ------------------------------------------------------------------ stopping

    [Fact]
    public async Task RemovingStopsThePushes_AndRegisteringAgainStartsThem_OnTheSameDevice()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(session, token);
        var id = (await RowAsync(token))!.Id;

        await RemoveAsync(session, token);

        var removed = await RowAsync(token);
        Assert.NotNull(removed!.DisabledAt);
        Assert.Equal("Removed", removed.DisabledReason);

        await TextAsync(session.UserId, "BOOKING_CONFIRMED:off");
        Assert.Empty(_expo.Sent);

        await RegisterAsync(session, token);

        var back = await RowAsync(token);
        Assert.Equal(id, back!.Id);
        Assert.Null(back.DisabledAt);
        Assert.Null(back.DisabledReason);

        await TextAsync(session.UserId, "BOOKING_CONFIRMED:on");
        Assert.Single(_expo.Sent);
    }

    [Fact]
    public async Task NobodyCanRemoveAnotherPersonsDevice_NorTellThatItExists()
    {
        var owner = await SignedInDonorAsync();
        var stranger = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(owner, token);

        await RemoveAsync(stranger, token);

        Assert.Null((await RowAsync(token))!.DisabledAt);
    }

    [Fact]
    public async Task RemovingATokenNobodyHasIsFine()
    {
        await RemoveAsync(await SignedInDonorAsync(), NewToken());
    }

    [Fact]
    public async Task TheTokenNeverTravelsInAUrl()
    {
        var routes = _host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText!)
            .Where(r => r.StartsWith(Devices, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(new[] { Devices, Remove }.Order(StringComparer.Ordinal), routes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("/api/auth/mobile/session/logout-all")]
    public async Task LogoutEverywhereDisablesEveryDeviceOfThatUser_AndNoOneElses(string path)
    {
        var session = await SignedInDonorAsync();
        var other = await SignedInDonorAsync();
        var (phone, tablet, theirs) = (NewToken(), NewToken(), NewToken());
        await RegisterAsync(session, phone);
        await RegisterAsync(session, tablet);
        await RegisterAsync(other, theirs);

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync(path, new { refreshToken = session.Refresh });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal("LogoutEverywhere", (await RowAsync(phone))!.DisabledReason);
        Assert.Equal("LogoutEverywhere", (await RowAsync(tablet))!.DisabledReason);
        Assert.Null((await RowAsync(theirs))!.DisabledAt);

        await TextAsync(session.UserId, "BOOKING_CONFIRMED:after-all");
        Assert.Empty(_expo.Sent);
    }

    [Fact]
    public async Task ALogoutEverywhereThatIsRefusedDisablesNothing()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(session, token);

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/mobile/session/logout-all", new { refreshToken = "not-a-live-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null((await RowAsync(token))!.DisabledAt);
    }

    // ------------------------------------------------------------------ the job, through the host

    [Fact]
    public async Task TheReceiptsJobRunsAsThePlatform_ThroughTheHostsOwnGateway()
    {
        var session = await SignedInDonorAsync();
        var token = NewToken();
        await RegisterAsync(session, token);
        _expo.OnSend = _ => FakeExpo.Ticket("job-ticket-" + session.UserId);
        await TextAsync(session.UserId, "BOOKING_CONFIRMED:job");

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE notify.Notifications SET SentAt = DATEADD(MINUTE, -20, SentAt) WHERE UserId = @user AND Channel = 'Push';",
            new { user = session.UserId });
        _expo.OnReceipts = ids => (HttpStatusCode.OK, """{"data":{"ID":{"status":"error","details":{"error":"DeviceNotRegistered"}}}}""".Replace("ID", ids[0], StringComparison.Ordinal));

        await new PushReceiptsJob(_host.Services.GetRequiredService<IServiceScopeFactory>()).RunAsync(CancellationToken.None);

        Assert.Equal("DeviceNotRegistered", (await RowAsync(token))!.DisabledReason);
    }
}

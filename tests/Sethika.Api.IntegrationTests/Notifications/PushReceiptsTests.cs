using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Infrastructure.Notifications;
using Xunit;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// Plan F, F29: the job that asks Expo what became of the pushes it took, against real rows in
/// <c>notify.Notifications</c> with only Expo's side of the HTTP call stubbed.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class PushReceiptsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string Prefix = "receipt-test-";

    private readonly Guid _user = Guid.CreateVersion7();

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await CleanAsync();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PreferredLanguage, Status, SecurityStamp)
            VALUES (@user, 'Donor', N'Receipt Donor', @phone, 'en', 'Active', 'stamp');
            """,
            new { user = _user, phone = "+9478" + Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture) });
    }

    public async Task DisposeAsync()
    {
        await CleanAsync();
        await ExecuteAsync(
            """
            DELETE FROM notify.MobileDevices WHERE UserId = @user;
            DELETE FROM auth.Users WHERE Id = @user;
            """,
            new { user = _user });
    }

    private Task CleanAsync() =>
        ExecuteAsync("DELETE FROM notify.Notifications WHERE Channel = 'Push' OR ProviderMessageId LIKE @prefix;", new { prefix = Prefix + "%" });

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private PushReceipts Receipts(FakeExpo expo, int batchSize = 1000)
    {
        var client = new HttpClient(expo) { BaseAddress = new Uri(new PushOptions().ExpoBaseUrl) };
        return new PushReceipts(
            new PlatformConnectionFactory(database.ConnectionString),
            new ExpoPushGateway(client),
            Options.Create(new PushOptions { ReceiptBatchSize = batchSize }),
            NullLogger<PushReceipts>.Instance);
    }

    /// <summary>A push row, sent <paramref name="minutesAgo"/> minutes ago.</summary>
    private async Task<string> TicketAsync(int minutesAgo = 20, string channel = "Push", string status = "Sent", string token = "ExponentPushToken[receipt]")
    {
        var ticket = Prefix + Guid.NewGuid().ToString("N");
        await ExecuteAsync(
            """
            INSERT notify.Notifications (UserId, Channel, Recipient, Body, Status, SentAt, ProviderMessageId)
            VALUES (@user, @channel, @token, N'body', @status, DATEADD(MINUTE, -@minutesAgo, SYSUTCDATETIME()), @ticket);
            """,
            new { user = _user, channel, token, status, minutesAgo, ticket });

        return ticket;
    }

    private async Task<int> DeviceAsync(string token)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT notify.MobileDevices (UserId, ExpoPushToken, Platform) OUTPUT inserted.Id VALUES (@user, @token, 'Android');
            """,
            new { user = _user, token });
    }

    private async Task<(string Status, string? Error)> RowAsync(string ticket)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<(string, string?)>(
            "SELECT Status, ErrorMessage FROM notify.Notifications WHERE ProviderMessageId = @ticket;",
            new { ticket });
    }

    private async Task<string?> DisabledReasonAsync(int device)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT DisabledReason FROM notify.MobileDevices WHERE Id = @device;",
            new { device });
    }

    private static string Answer(params (string Ticket, string Receipt)[] receipts) =>
        JsonSerializer.Serialize(new
        {
            data = receipts.ToDictionary(r => r.Ticket, r => JsonElement.Parse(r.Receipt), StringComparer.Ordinal),
        });

    private const string Ok = """{"status":"ok"}""";

    private static string Refused(string error) =>
        $$$"""{"status":"error","message":"refused","details":{"error":"{{{error}}}"}}""";

    [Fact]
    public async Task AGoodReceiptMovesTheRowToDelivered()
    {
        var ticket = await TicketAsync();
        var expo = new FakeExpo { OnReceipts = _ => (HttpStatusCode.OK, Answer((ticket, Ok))) };

        await Receipts(expo).CheckAsync();

        Assert.Equal(("Delivered", null), await RowAsync(ticket));
    }

    [Fact]
    public async Task AReceiptSayingTheDeviceIsGone_FailsTheRowAndDisablesTheDevice()
    {
        const string token = "ExponentPushToken[gone-on-receipt]";
        var device = await DeviceAsync(token);
        var other = await DeviceAsync("ExponentPushToken[still-here]");
        var ticket = await TicketAsync(token: token);
        var expo = new FakeExpo { OnReceipts = _ => (HttpStatusCode.OK, Answer((ticket, Refused("DeviceNotRegistered")))) };

        await Receipts(expo).CheckAsync();

        Assert.Equal(("Failed", "DeviceNotRegistered"), await RowAsync(ticket));
        Assert.Equal("DeviceNotRegistered", await DisabledReasonAsync(device));
        Assert.Null(await DisabledReasonAsync(other));
    }

    [Fact]
    public async Task AnyOtherBadReceiptFailsTheRow_AndLeavesTheDeviceEnabled()
    {
        const string token = "ExponentPushToken[too-big]";
        var device = await DeviceAsync(token);
        var ticket = await TicketAsync(token: token);
        var expo = new FakeExpo { OnReceipts = _ => (HttpStatusCode.OK, Answer((ticket, Refused("MessageTooBig")))) };

        await Receipts(expo).CheckAsync();

        Assert.Equal(("Failed", "MessageTooBig"), await RowAsync(ticket));
        Assert.Null(await DisabledReasonAsync(device));
    }

    [Fact]
    public async Task AReceiptNotReadyLeavesTheRowSent_AndTheNextRunAsksAgain()
    {
        var ticket = await TicketAsync();
        var expo = new FakeExpo();

        await Receipts(expo).CheckAsync();
        Assert.Equal(("Sent", null), await RowAsync(ticket));

        expo.OnReceipts = _ => (HttpStatusCode.OK, Answer((ticket, Ok)));
        await Receipts(expo).CheckAsync();

        Assert.Equal(("Delivered", null), await RowAsync(ticket));
    }

    [Fact]
    public async Task OnlyPushesSentBetweenFifteenMinutesAndADayAgoAreAskedAbout()
    {
        var due = await TicketAsync(minutesAgo: 16);
        var lastChance = await TicketAsync(minutesAgo: (24 * 60) - 1);
        await TicketAsync(minutesAgo: 14);
        await TicketAsync(minutesAgo: (24 * 60) + 1);
        await TicketAsync(channel: "SMS");
        await TicketAsync(status: "Delivered");
        await TicketAsync(status: "Failed");
        var expo = new FakeExpo();

        await Receipts(expo).CheckAsync();

        Assert.Equal(
            new[] { due, lastChance }.Order(StringComparer.Ordinal),
            Assert.Single(expo.ReceiptCalls).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARowWithNoTicketIsNotAskedAbout()
    {
        await ExecuteAsync(
            """
            INSERT notify.Notifications (Channel, Recipient, Body, Status, SentAt)
            VALUES ('Push', 'ExponentPushToken[no-ticket]', N'body', 'Sent', DATEADD(MINUTE, -20, SYSUTCDATETIME()));
            """);
        var expo = new FakeExpo();

        await Receipts(expo).CheckAsync();

        Assert.Empty(expo.ReceiptCalls);
    }

    [Fact]
    public async Task EveryRowIsAskedAboutOnce_InBatches_EvenWhenNoneIsReady()
    {
        var tickets = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            tickets.Add(await TicketAsync());
        }

        var expo = new FakeExpo();

        await Receipts(expo, batchSize: 2).CheckAsync();

        Assert.Equal([2, 2, 1], expo.ReceiptCalls.Select(c => c.Count));
        Assert.Equal(tickets.Order(StringComparer.Ordinal), expo.ReceiptCalls.SelectMany(c => c).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AFailedCallChangesNothing_AndIsLeftToTheNextRun()
    {
        var ticket = await TicketAsync();
        var expo = new FakeExpo { OnReceipts = _ => (HttpStatusCode.ServiceUnavailable, "{}") };

        await Assert.ThrowsAsync<HttpRequestException>(() => Receipts(expo).CheckAsync());

        Assert.Equal(("Sent", null), await RowAsync(ticket));
    }
}

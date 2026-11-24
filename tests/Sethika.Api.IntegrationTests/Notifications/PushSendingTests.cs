using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Xunit;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// Plan F, F26-F29: the push that follows a text, against real SQL Server, with only Expo's side of
/// the HTTP call stubbed. The dedup index, the device rows and the delivery log are all the real ones.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed partial class PushSendingTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string Confirmed = "BOOKING_CONFIRMED";
    private const string OutboxEvent = "Test.Push.Text";

    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["bookingNo"] = "BK-2026-000451",
        ["institution"] = "Alpha Vihara",
        ["date"] = "2026-11-14",
        ["time"] = "11:30",
    };

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private readonly RecordingLogger<PushingUserTextMessages> _log = new();

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;");
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(
            """
            UPDATE notify.Templates SET IsActive = 1 WHERE Channel = 'Push';
            DELETE FROM core.OutboxMessages WHERE EventType = @OutboxEvent;
            DELETE FROM notify.Notifications WHERE UserId IN @users OR Recipient IN @phones OR DedupKey LIKE 'push:race:%';
            DELETE FROM notify.MobileDevices WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { OutboxEvent, users = _users, phones = _phones });
    }

    // ------------------------------------------------------------------ plumbing

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private PlatformConnectionFactory Connections() => new(database.ConnectionString);

    private static ExpoPushGateway Gateway(FakeExpo expo)
    {
        var client = new HttpClient(expo) { BaseAddress = new Uri(new PushOptions().ExpoBaseUrl) };
        return new ExpoPushGateway(client);
    }

    private PushingUserTextMessages Texts(FakeExpo expo, ISmsSender? sms = null)
    {
        var connections = Connections();
        sms ??= new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        return new PushingUserTextMessages(new UserTextMessages(connections, sms, NullLogger<UserTextMessages>.Instance), connections, Gateway(expo), _log);
    }

    private async Task<Guid> DonorAsync(string language = "en", bool phoneConfirmed = true, bool deleted = false)
    {
        var id = Guid.CreateVersion7();
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp, IsDeleted)
            VALUES (@id, 'Donor', N'Push Donor', @phone, @phoneConfirmed, @language, 'Active', 'stamp', @deleted);
            """,
            new { id, phone, phoneConfirmed, language, deleted });

        return id;
    }

    private static string NewToken() => $"ExponentPushToken[{Guid.NewGuid():N}]";

    private async Task<(int Id, string Token)> DeviceAsync(Guid user, string? disabledReason = null)
    {
        var token = NewToken();
        var id = await ScalarAsync<int>(
            """
            INSERT notify.MobileDevices (UserId, ExpoPushToken, Platform, DisabledAt, DisabledReason)
            OUTPUT inserted.Id
            VALUES (@user, @token, 'Android', CASE WHEN @disabledReason IS NULL THEN NULL ELSE SYSUTCDATETIME() END, @disabledReason);
            """,
            new { user, token, disabledReason });

        return (id, token);
    }

    private sealed record Row(string Status, string Recipient, string? Subject, string Body, string DedupKey, string? ProviderMessageId, string? ErrorMessage, short? TemplateId);

    private async Task<List<Row>> PushRowsAsync(Guid user)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<Row>(
            """
            SELECT Status, Recipient, Subject, Body, DedupKey, ProviderMessageId, ErrorMessage, TemplateId
            FROM notify.Notifications
            WHERE UserId = @user AND Channel = 'Push'
            ORDER BY Id;
            """,
            new { user })];
    }

    private Task<string?> DisabledReasonAsync(int device) =>
        ScalarAsync<string?>("SELECT DisabledReason FROM notify.MobileDevices WHERE Id = @device;", new { device });

    private static async Task SendAsync(PushingUserTextMessages texts, Guid user, string code = Confirmed, string key = "BOOKING_CONFIRMED:BK-2026-000451") =>
        await texts.SendAsync(user, code, Values, key);

    /// <summary>The processor, with one handler that sends <paramref name="user"/> the confirmation text.</summary>
    private OutboxProcessor Processor(IUserTextMessages texts, Guid user) =>
        new(Connections(), [new TextOnEvent(texts, user)], Options.Create(new OutboxOptions()), NullLogger<OutboxProcessor>.Instance);

    private Task QueueOutboxEventAsync() =>
        ExecuteAsync(
            "INSERT core.OutboxMessages (EventType, AggregateType, AggregateId, Payload) VALUES (@OutboxEvent, 'Test', 'x', N'{}');",
            new { OutboxEvent });

    private sealed class TextOnEvent(IUserTextMessages texts, Guid user) : IOutboxHandler
    {
        public IReadOnlyCollection<string> EventTypes { get; } = [OutboxEvent];

        public Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken) =>
            texts.SendAsync(user, Confirmed, Values, "BOOKING_CONFIRMED:outbox", cancellationToken);
    }

    /// <summary>A real sender, delivery log and all, whose gateway is down for the first text.</summary>
    private sealed class SmsDownOnce(IDbConnectionFactory connections) : SmsSenderBase(connections, new SmsOptions())
    {
        private int _attempts;

        protected override Task<string?> DeliverAsync(string phoneNumber, string message, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _attempts) == 1
                ? throw new HttpRequestException("The SMS gateway is down.")
                : Task.FromResult<string?>("sms-1");
    }

    // ------------------------------------------------------------------ what goes out

    [Fact]
    public async Task EachDeviceGetsOnePush_InTheUsersLanguage_WithItsOwnRow()
    {
        var user = await DonorAsync("si");
        var first = await DeviceAsync(user);
        var second = await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Equal(
            new[] { first.Token, second.Token }.Order(StringComparer.Ordinal),
            expo.Sent.Select(m => m.GetProperty("to").GetString()!).Order(StringComparer.Ordinal));

        var title = await ScalarAsync<string>("SELECT Subject FROM notify.Templates WHERE Code = @Confirmed AND Channel = 'Push' AND Language = 'si';", new { Confirmed });
        var message = expo.Sent[0];
        Assert.Equal(title, message.GetProperty("title").GetString());
        Assert.Equal("Alpha Vihara, 2026-11-14 11:30. ඔබගේ වෙන්කිරීම තහවුරුයි. ස්තූතියි.", message.GetProperty("body").GetString());

        var rows = await PushRowsAsync(user);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Sent", r.Status);
            Assert.Equal(title, r.Subject);
            Assert.NotNull(r.TemplateId);
            Assert.True(Guid.TryParse(r.ProviderMessageId, out _));
        });
        Assert.Contains(rows, r => r.Recipient == first.Token && r.DedupKey == $"push:BOOKING_CONFIRMED:BK-2026-000451:{first.Id}");
        Assert.Contains(rows, r => r.Recipient == second.Token && r.DedupKey == $"push:BOOKING_CONFIRMED:BK-2026-000451:{second.Id}");
    }

    [Fact]
    public async Task WhatExpoIsHanded_IsWhatItsDocumentationAsksFor()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        var message = Assert.Single(expo.Sent);
        Assert.Equal("Booking confirmed", message.GetProperty("title").GetString());
        Assert.Equal(Confirmed, message.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal(ExpoPushGateway.AndroidChannelId, message.GetProperty("channelId").GetString());
        Assert.Equal(86_400, message.GetProperty("ttl").GetInt32());
        Assert.Equal("high", message.GetProperty("priority").GetString());
        Assert.Equal("default", message.GetProperty("sound").GetString());
    }

    [Fact]
    public async Task ALanguageWithNoActiveTemplateFallsBackToEnglish()
    {
        var user = await DonorAsync("ta");
        await DeviceAsync(user);
        await ExecuteAsync("UPDATE notify.Templates SET IsActive = 0 WHERE Code = @Confirmed AND Channel = 'Push' AND Language = 'ta';", new { Confirmed });
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Equal("Booking confirmed", Assert.Single(expo.Sent).GetProperty("title").GetString());
    }

    [Fact]
    public async Task ACodeWithNoPushTemplate_SendsItsSmsAndNoPush()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user, "BOOKING_REQUESTED", "BOOKING_REQUESTED:BK-1");

        Assert.Empty(expo.Sent);
        Assert.Empty(await PushRowsAsync(user));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM notify.Notifications WHERE DedupKey = 'BOOKING_REQUESTED:BK-1' AND Recipient IN @phones;", new { phones = _phones }));
    }

    [Fact]
    public async Task AnInactivePushTemplateIsNoPushTemplate()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        await ExecuteAsync("UPDATE notify.Templates SET IsActive = 0 WHERE Code = @Confirmed AND Channel = 'Push';", new { Confirmed });
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Empty(expo.Sent);
    }

    [Fact]
    public async Task ADisabledDeviceIsNotPushedTo_AndAnEnabledOneBesideItIs()
    {
        var user = await DonorAsync();
        await DeviceAsync(user, "Removed");
        var enabled = await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Equal(enabled.Token, Assert.Single(expo.Sent).GetProperty("to").GetString());
    }

    [Fact]
    public async Task AnotherUsersDeviceIsNotPushedTo()
    {
        var user = await DonorAsync();
        await DeviceAsync(await DonorAsync());
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Empty(expo.Sent);
    }

    [Fact]
    public async Task AUserWithNoConfirmedPhoneIsStillPushedTo_AndTheOutcomeIsTheSmss()
    {
        var user = await DonorAsync(phoneConfirmed: false);
        await DeviceAsync(user);
        var expo = new FakeExpo();

        Assert.Equal(UserTextOutcome.NoPhone, await Texts(expo).SendAsync(user, Confirmed, Values, "BOOKING_CONFIRMED:np"));

        Assert.Single(expo.Sent);
    }

    [Fact]
    public async Task ADeletedUserIsNotPushedTo()
    {
        var user = await DonorAsync(deleted: true);
        await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);

        Assert.Empty(expo.Sent);
    }

    [Fact]
    public async Task ATextToARosterNumberIsNotPushed()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        var phone = _phones[^1];
        var expo = new FakeExpo();

        await Texts(expo).SendToPhoneAsync(phone, "en", Confirmed, Values, "BOOKING_CONFIRMED:roster");

        Assert.Empty(expo.Sent);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM notify.Notifications WHERE DedupKey = 'BOOKING_CONFIRMED:roster' AND Recipient = @phone;", new { phone }));
    }

    // ------------------------------------------------------------------ never twice

    [Fact]
    public async Task ARerunSendsNothing()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        await DeviceAsync(user);
        var expo = new FakeExpo();

        await SendAsync(Texts(expo), user);
        await SendAsync(Texts(expo), user);

        Assert.Equal(2, expo.Sent.Count);
        Assert.Equal(2, (await PushRowsAsync(user)).Count);
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ARerunStillReachesADeviceThatHasNotHadThePush()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        var expo = new FakeExpo();
        await SendAsync(Texts(expo), user);

        var added = await DeviceAsync(user);
        await SendAsync(Texts(expo), user);

        Assert.Equal(2, expo.Sent.Count);
        Assert.Equal(added.Token, expo.Sent[1].GetProperty("to").GetString());
    }

    /// <summary>
    /// Forces the race rather than hoping for it: a transaction holds the key, both sends queue behind
    /// it on the unique index, and then it lets go, so both inserts are in flight at the same moment.
    /// </summary>
    [Fact]
    public async Task AForcedRaceSendsOnce()
    {
        var user = await DonorAsync();
        var device = await DeviceAsync(user);
        var key = "race:" + Guid.NewGuid().ToString("N");
        var expo = new FakeExpo();

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "INSERT notify.Notifications (Channel, Recipient, Body, DedupKey) VALUES ('Push', 'holder', N'holder', @dedupKey);",
            new { dedupKey = $"push:{key}:{device.Id}" },
            transaction);

        var sends = new[] { SendAsync(Texts(expo), user, key: key), SendAsync(Texts(expo), user, key: key) };

        await WaitForBlockedAsync(2);
        Assert.Empty(expo.Sent);

        await transaction.RollbackAsync();
        await Task.WhenAll(sends);

        Assert.Single(expo.Sent);
        Assert.Single(await PushRowsAsync(user));
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    private async Task WaitForBlockedAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_status = 'WAIT' AND resource_database_id = DB_ID();") < count)
        {
            Assert.True(DateTime.UtcNow < deadline, "The sends never queued behind the held key.");
            await Task.Delay(50);
        }
    }

    // ------------------------------------------------------------------ SMS first, and never held back by a push

    [Fact]
    public async Task AFailedSmsFailsTheMessageWithNoPush_AndTheRetryPushesOnce()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        await QueueOutboxEventAsync();
        var expo = new FakeExpo();
        var texts = Texts(expo, new SmsDownOnce(Connections()));

        var failed = await Processor(texts, user).ProcessBatchAsync();

        Assert.Equal(1, failed.Failed);
        Assert.Empty(expo.Sent);

        var retried = await Processor(texts, user).ProcessBatchAsync();

        Assert.Equal((1, 0), (retried.Handled, retried.Failed));
        Assert.Single(expo.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"errors":[{"code":"INTERNAL","message":"down"}]}""", "HTTP 500")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"errors":[{"code":"TOO_MANY_REQUESTS","message":"slow down"}]}""", "HTTP 429")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>", "invalid")]
    [InlineData(HttpStatusCode.OK, """{"data":[{"status":"ok"}]}""", "no ticket id")]
    public async Task AFailedPush_IsRecordedAndLogged_AndFailsNeitherTheSmsNorTheMessage(HttpStatusCode status, string reply, string reason)
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        await QueueOutboxEventAsync();
        var expo = new FakeExpo { OnSend = _ => (status, reply) };

        var run = await Processor(Texts(expo), user).ProcessBatchAsync();

        Assert.Equal((1, 0), (run.Handled, run.Failed));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM notify.Notifications WHERE DedupKey = 'BOOKING_CONFIRMED:outbox' AND Recipient IN @phones AND Status = 'Sent';", new { phones = _phones }));

        var row = Assert.Single(await PushRowsAsync(user));
        Assert.Equal("Failed", row.Status);
        Assert.Contains(reason, row.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AFailedPushIsNotRetried_AndTheNextDeviceStillGetsItsOwn()
    {
        var user = await DonorAsync();
        var broken = await DeviceAsync(user);
        var working = await DeviceAsync(user);
        var expo = new FakeExpo { OnSend = token => token == broken.Token ? (HttpStatusCode.BadGateway, "{}") : FakeExpo.Ticket("t1") };

        await SendAsync(Texts(expo), user);
        Assert.Equal(2, expo.Sent.Count);

        await SendAsync(Texts(expo), user);
        Assert.Equal(2, expo.Sent.Count);

        var rows = await PushRowsAsync(user);
        Assert.Equal("Failed", rows.Single(r => r.Recipient == broken.Token).Status);
        Assert.Equal("Sent", rows.Single(r => r.Recipient == working.Token).Status);
    }

    [Fact]
    public async Task APushThatCannotEvenBeLookedUp_LeavesTheSmsStanding()
    {
        var user = await DonorAsync();
        var connections = Connections();
        var sms = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        var texts = new PushingUserTextMessages(
            new UserTextMessages(connections, sms, NullLogger<UserTextMessages>.Instance),
            new NoConnections(),
            Gateway(new FakeExpo()),
            _log);

        Assert.Equal(UserTextOutcome.Sent, await texts.SendAsync(user, Confirmed, Values, "BOOKING_CONFIRMED:nolookup"));

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error);
    }

    private sealed class NoConnections : IDbConnectionFactory
    {
        public Task<System.Data.Common.DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The database went away.");
    }

    // ------------------------------------------------------------------ what Expo says back

    [Fact]
    public async Task ATicketSayingTheDeviceIsGone_DisablesIt_AndTheNextTextSkipsIt()
    {
        var user = await DonorAsync();
        var device = await DeviceAsync(user);
        var expo = new FakeExpo { OnSend = token => FakeExpo.Refusal(token, "DeviceNotRegistered") };

        await SendAsync(Texts(expo), user);

        Assert.Equal("DeviceNotRegistered", await DisabledReasonAsync(device.Id));
        var row = Assert.Single(await PushRowsAsync(user));
        Assert.Equal(("Failed", "DeviceNotRegistered"), (row.Status, row.ErrorMessage));

        await SendAsync(Texts(expo), user, key: "BOOKING_CONFIRMED:next");

        Assert.Single(expo.Sent);
    }

    [Fact]
    public async Task AnyOtherRefusalLeavesTheDeviceEnabled()
    {
        var user = await DonorAsync();
        var device = await DeviceAsync(user);
        var expo = new FakeExpo { OnSend = token => FakeExpo.Refusal(token, "MessageRateExceeded") };

        await SendAsync(Texts(expo), user);

        Assert.Null(await DisabledReasonAsync(device.Id));
        Assert.Equal("Failed", Assert.Single(await PushRowsAsync(user)).Status);
    }

    [Fact]
    public async Task ATicketThatComesBackAsAnObject_NotAnArray_IsRead()
    {
        var user = await DonorAsync();
        await DeviceAsync(user);
        var expo = new FakeExpo { OnSend = _ => (HttpStatusCode.OK, """{"data":{"status":"ok","id":"single-ticket"}}""") };

        await SendAsync(Texts(expo), user);

        Assert.Equal("single-ticket", Assert.Single(await PushRowsAsync(user)).ProviderMessageId);
    }

    [Theory]
    [InlineData("""{"data":[{"status":"error","message":"\"TOKEN\" was refused"}]}""")]
    [InlineData("""{"data":[{"status":"error","message":"\"TOKEN\" was refused","details":{}}]}""")]
    public async Task ARefusalThatQuotesTheToken_KeepsItOutOfTheLogAndTheErrorColumn(string reply)
    {
        var user = await DonorAsync();
        var device = await DeviceAsync(user);
        var expo = new FakeExpo { OnSend = token => (HttpStatusCode.OK, reply.Replace("TOKEN", token, StringComparison.Ordinal)) };

        await SendAsync(Texts(expo), user);

        var row = Assert.Single(await PushRowsAsync(user));
        Assert.Equal("Failed", row.Status);
        Assert.DoesNotContain(device.Token, row.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("[token]", row.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(_log.Entries, e => e.Message.Contains("[token]", StringComparison.Ordinal));
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains(device.Token, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the table

    [Fact]
    public async Task TheDatabaseRefusesAPlatformOtherThanAndroidOrIos()
    {
        var user = await DonorAsync();

        var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "INSERT notify.MobileDevices (UserId, ExpoPushToken, Platform) VALUES (@user, @token, 'Windows');",
            new { user, token = NewToken() }));

        Assert.Contains("CK_MobileDevices_Platform", refused.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the seed (F27)

    [Fact]
    public async Task ExactlyFiveCodesAreSeededInThreeLanguages_WithATitle_AndOnlyTheBookingValues()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var rows = (await connection.QueryAsync<(string Code, string Language, string? Subject, string Body)>(
            "SELECT Code, Language, Subject, Body FROM notify.Templates WHERE Channel = 'Push';")).AsList();

        Assert.Equal(
            ["BOOKING_APPROVED", "BOOKING_CANCELLED", "BOOKING_CONFIRMED", "BOOKING_REJECTED", "BOOKING_REMINDER"],
            rows.Select(r => r.Code).Distinct().Order(StringComparer.Ordinal));

        foreach (var group in rows.GroupBy(r => r.Code))
        {
            Assert.Equal(["en", "si", "ta"], group.Select(r => r.Language).Order(StringComparer.Ordinal));
        }

        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Subject));
            Assert.All(Placeholder().Matches(row.Subject + row.Body), match => Assert.Contains(match.Groups[1].Value, Values.Keys));
        });
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}

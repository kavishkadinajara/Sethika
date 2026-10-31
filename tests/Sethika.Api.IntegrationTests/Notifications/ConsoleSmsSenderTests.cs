using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Infrastructure.Notifications;
using Xunit;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// Exercises the part of sending that is shared by both senders: the notify.Notifications row, the
/// dedup index, and the segment and cost columns.
/// </summary>
/// <remarks>
/// Deliberately against real SQL. The dedup guarantee is enforced by UX_Notifications_Dedup, so a
/// test with a fake repository would prove only that the C# agrees with itself.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ConsoleSmsSenderTests(SethikaTestDatabase database)
{
    private ConsoleSmsSender CreateSender(Action<SmsOptions>? configure = null)
    {
        var options = new SmsOptions();
        configure?.Invoke(options);

        return new ConsoleSmsSender(
            new PlatformConnectionFactory(database.ConnectionString),
            Options.Create(options),
            NullLogger<ConsoleSmsSender>.Instance);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string NewPhoneNumber() =>
        "+9477" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task RecordsTheMessageAsSent()
    {
        var phone = NewPhoneNumber();

        await CreateSender().SendAsync(phone, "Your Sethika verification code is 123456");

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(string Channel, string Status, string Body, string? ProviderMessageId, DateTime? SentAt)>(
            """
            SELECT Channel, Status, Body, ProviderMessageId, SentAt
            FROM notify.Notifications WHERE Recipient = @phone;
            """,
            new { phone });

        Assert.Equal("SMS", row.Channel);
        Assert.Equal("Sent", row.Status);
        Assert.Contains("123456", row.Body, StringComparison.Ordinal);
        Assert.NotNull(row.SentAt);

        // Populated in development too, so code that reads it is not first exercised in production.
        Assert.NotNull(row.ProviderMessageId);
    }

    [Fact]
    public async Task SendsOnlyOnceForARepeatedDedupKey()
    {
        // The guarantee a retried Hangfire job depends on: a reminder is never texted twice.
        var phone = NewPhoneNumber();
        var dedupKey = "TEST:" + Guid.CreateVersion7().ToString("N")[..20];
        var sender = CreateSender();

        await sender.SendAsync(phone, "first", dedupKey);
        await sender.SendAsync(phone, "second", dedupKey);

        await using var connection = await OpenAsync();
        var rows = await connection.QueryAsync<string>(
            "SELECT Body FROM notify.Notifications WHERE DedupKey = @dedupKey;",
            new { dedupKey });

        var bodies = rows.ToArray();

        Assert.Single(bodies);
        Assert.Equal("first", bodies[0]);
    }

    [Fact]
    public async Task ConcurrentSendsWithOneDedupKeyProduceExactlyOneRow()
    {
        // The reason uniqueness is enforced by the index rather than a prior SELECT. Checking first
        // and inserting second is a race that both workers win.
        var dedupKey = "RACE:" + Guid.CreateVersion7().ToString("N")[..20];
        var sender = CreateSender();

        var sends = Enumerable.Range(0, 8)
            .Select(i => sender.SendAsync(NewPhoneNumber(), $"body {i}", dedupKey));

        await Task.WhenAll(sends);

        await using var connection = await OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notify.Notifications WHERE DedupKey = @dedupKey;",
            new { dedupKey });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SendsEveryMessageWhenNoDedupKeyIsGiven()
    {
        // A null dedup key must not collide with another null - the filtered unique index excludes
        // nulls, and the insert's WHERE has to as well or only one OTP would ever be sent.
        var phone = NewPhoneNumber();
        var sender = CreateSender();

        await sender.SendAsync(phone, "one");
        await sender.SendAsync(phone, "two");

        await using var connection = await OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notify.Notifications WHERE Recipient = @phone;",
            new { phone });

        Assert.Equal(2, count);
    }

    [Theory]
    // English stays GSM-7: 160 characters per segment.
    [InlineData("short english message", 1, 0.50)]
    [InlineData(null, 2, 1.00)]                     // 161 chars, built in the test
    public async Task CostsGsm7MessagesAtOneHundredAndSixtyCharactersPerSegment(
        string? message,
        int expectedSegments,
        decimal expectedCost)
    {
        var phone = NewPhoneNumber();
        var body = message ?? new string('a', 161);

        await CreateSender().SendAsync(phone, body);

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(byte? SmsSegments, decimal? CostLkr)>(
            "SELECT SmsSegments, CostLkr FROM notify.Notifications WHERE Recipient = @phone;",
            new { phone });

        Assert.Equal((byte)expectedSegments, row.SmsSegments);
        Assert.Equal(expectedCost, row.CostLkr);
    }

    [Fact]
    public async Task CostsASinhalaMessageAtSeventyCharactersPerSegment()
    {
        // The case that matters for this platform: Sinhala forces UCS-2, so the segment drops from
        // 160 to 70 and the same message costs more. Charging it as GSM-7 would understate the SMS
        // quota for precisely the users Sethika is built for.
        var phone = NewPhoneNumber();
        var body = new string('අ', 71);

        await CreateSender().SendAsync(phone, body);

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(byte? SmsSegments, decimal? CostLkr)>(
            "SELECT SmsSegments, CostLkr FROM notify.Notifications WHERE Recipient = @phone;",
            new { phone });

        Assert.Equal((byte)2, row.SmsSegments);
        Assert.Equal(1.00m, row.CostLkr);
    }

    [Fact]
    public async Task OneNonAsciiCharacterMakesTheWholeMessageUnicode()
    {
        // 71 characters, only one of them Sinhala. The carrier encodes the entire message as UCS-2,
        // so it is two segments - not one, as a per-character estimate would conclude.
        var phone = NewPhoneNumber();
        var body = new string('a', 70) + "අ";

        await CreateSender().SendAsync(phone, body);

        await using var connection = await OpenAsync();
        var segments = await connection.ExecuteScalarAsync<byte?>(
            "SELECT SmsSegments FROM notify.Notifications WHERE Recipient = @phone;",
            new { phone });

        Assert.Equal((byte)2, segments);
    }
}

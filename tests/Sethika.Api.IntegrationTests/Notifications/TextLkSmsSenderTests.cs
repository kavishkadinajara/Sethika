using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Infrastructure.Notifications;
using Xunit;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// What Text.lk is sent, and how its answer is recorded in notify.Notifications.
/// </summary>
/// <remarks>
/// The gateway is replaced by a handler that captures the request and returns a canned reply. The
/// reply shapes are the two that Text.lk's own documentation gives. Everything on our side of the
/// wire is real, including SQL Server, because "sent" or "failed" is a row in the delivery log.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class TextLkSmsSenderTests(SethikaTestDatabase database)
{
    private const string Token = "test-token-not-a-secret";

    private sealed class FakeGateway(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private const string SuccessWithUid =
        """{"status":"success","message":"Your message was successfully delivered","data":{"uid":"6512abcd","to":"94771234567","status":"Delivered"}}""";

    private const string SuccessWithSmsId =
        """{"status":true,"message":"SMS queued successfully","data":{"sms_id":"abc123","recipient":"94771234567"}}""";

    private (TextLkSmsSender Sender, FakeGateway Gateway) Create(
        HttpStatusCode status,
        string reply,
        string? token = Token)
    {
        var gateway = new FakeGateway(status, reply);
        var options = new SmsOptions { TextLkApiToken = token };
        var client = new HttpClient(gateway) { BaseAddress = new Uri(options.TextLkBaseUrl) };

        return (new TextLkSmsSender(new PlatformConnectionFactory(database.ConnectionString), Options.Create(options), client), gateway);
    }

    private static string NewPhoneNumber() =>
        "+9477" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(string Status, string? ProviderMessageId, string? ErrorMessage)> RowFor(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        return await connection.QuerySingleAsync<(string, string?, string?)>(
            "SELECT Status, ProviderMessageId, ErrorMessage FROM notify.Notifications WHERE Recipient = @phone;",
            new { phone });
    }

    [Fact]
    public async Task PostsToTheV3SendEndpointWithABearerToken()
    {
        var (sender, gateway) = Create(HttpStatusCode.OK, SuccessWithUid);

        await sender.SendAsync(NewPhoneNumber(), "Your Sethika code is 123456");

        var request = Assert.IsType<HttpRequestMessage>(gateway.Request);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://app.text.lk/api/v3/sms/send"), request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Token, request.Headers.Authorization?.Parameter);

        // The token travels only in the header: a query string would put it in every request log.
        Assert.DoesNotContain(Token, request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task SendsTheNumberWithoutThePlus_TheSenderIdAndTheMessageUnchanged()
    {
        var (sender, gateway) = Create(HttpStatusCode.OK, SuccessWithUid);
        var phone = NewPhoneNumber();
        const string sinhala = "ඔබේ සේතික කේතය 123456";

        await sender.SendAsync(phone, sinhala);

        using var body = JsonDocument.Parse(gateway.RequestBody!);
        var root = body.RootElement;
        Assert.Equal(phone[1..], root.GetProperty("recipient").GetString());
        Assert.Equal("Sethika", root.GetProperty("sender_id").GetString());
        Assert.Equal("plain", root.GetProperty("type").GetString());
        Assert.Equal(sinhala, root.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(SuccessWithUid, "6512abcd")]
    [InlineData(SuccessWithSmsId, "abc123")]
    public async Task RecordsEitherDocumentedSuccessShapeAsSent(string reply, string expectedId)
    {
        var (sender, _) = Create(HttpStatusCode.OK, reply);
        var phone = NewPhoneNumber();

        await sender.SendAsync(phone, "Your Sethika code is 123456");

        var row = await RowFor(phone);
        Assert.Equal("Sent", row.Status);
        Assert.Equal(expectedId, row.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":"error","message":"Invalid recipient number"}""", "Invalid recipient number")]
    [InlineData(HttpStatusCode.OK, """{"status":false,"message":"Insufficient balance"}""", "Insufficient balance")]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"error","message":"Unauthenticated."}""", "HTTP 401")]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "no reason given")]

    // A proxy's 5xx in front of a body that looks like success is still not a delivery.
    [InlineData(HttpStatusCode.ServiceUnavailable, SuccessWithUid, "HTTP 503")]

    // A refusal that echoes the message must not copy the code into the failure column.
    [InlineData(HttpStatusCode.OK, """{"status":"error","message":"Invalid sender id","data":{"message":"Your Sethika code is 123456"}}""", "Invalid sender id")]
    public async Task RecordsARefusalAsFailed_WithTheGatewaysReason_AndThrows(
        HttpStatusCode status,
        string reply,
        string expectedInError)
    {
        // HTTP 200 with "status": "error" is the case that matters: trusting the status code alone
        // would record a refused OTP as sent, and the user would wait for a code that never comes.
        var (sender, _) = Create(status, reply);
        var phone = NewPhoneNumber();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(phone, "Your Sethika code is 123456"));

        Assert.Contains(expectedInError, ex.Message, StringComparison.Ordinal);
        var row = await RowFor(phone);
        Assert.Equal("Failed", row.Status);
        Assert.Contains(expectedInError, row.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", row.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Refuses the first <paramref name="failures"/> sends, then accepts, counting every request.</summary>
    private sealed class FlakyGateway(int failures, int holdRequest = 0) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        /// <summary>Set when request number <c>holdRequest</c> arrives; it then waits for <see cref="Release"/>.</summary>
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _requests);
            if (n == holdRequest)
            {
                Held.SetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            var reply = n <= failures
                ? """{"status":"error","message":"Insufficient balance"}"""
                : SuccessWithUid;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply, Encoding.UTF8, "application/json"),
            };
        }
    }

    private TextLkSmsSender Create(FlakyGateway gateway)
    {
        var options = new SmsOptions { TextLkApiToken = Token };
        var client = new HttpClient(gateway) { BaseAddress = new Uri(options.TextLkBaseUrl) };
        return new TextLkSmsSender(new PlatformConnectionFactory(database.ConnectionString), Options.Create(options), client);
    }

    private static string NewDedupKey() => "RETRY:" + Guid.CreateVersion7().ToString("N")[..20];

    private async Task<(string Recipient, string Body, string Status, byte Attempts, byte? SmsSegments, decimal? CostLkr, string? ProviderMessageId, string? ErrorMessage)[]> RowsFor(string dedupKey)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<(string, string, string, byte, byte?, decimal?, string?, string?)>(
            """
            SELECT Recipient, Body, Status, Attempts, SmsSegments, CostLkr, ProviderMessageId, ErrorMessage
            FROM notify.Notifications WHERE DedupKey = @dedupKey;
            """,
            new { dedupKey })).ToArray();
    }

    [Fact]
    public async Task AFailedSendIsSentAgainOnRetry_OnItsOwnRow()
    {
        // Plan F, 10.6: the retry's insert was refused by the key and returned quietly, so a failed
        // text was never sent. The row is taken back and the text goes, still only once.
        var gateway = new FlakyGateway(failures: 1);
        var sender = Create(gateway);
        var phone = NewPhoneNumber();
        var key = NewDedupKey();

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(phone, new string('a', 161), key));
        var failed = Assert.Single(await RowsFor(key));
        Assert.Equal(("Failed", (byte?)2, (decimal?)1.00m), (failed.Status, failed.SmsSegments, failed.CostLkr));

        // The retry records what it sent: here another number and a Sinhala message (one segment of 70).
        var newPhone = NewPhoneNumber();
        const string sinhala = "ඔබේ වෙන්කිරීම තහවුරුයි";
        await sender.SendAsync(newPhone, sinhala, key);

        var row = Assert.Single(await RowsFor(key));
        Assert.Equal((newPhone, sinhala, "Sent", (byte)2), (row.Recipient, row.Body, row.Status, row.Attempts));
        Assert.Equal(((byte?)1, (decimal?)0.50m), (row.SmsSegments, row.CostLkr));
        Assert.Equal("6512abcd", row.ProviderMessageId);
        Assert.Null(row.ErrorMessage);
        Assert.Equal(2, gateway.Requests);

        // Sent now, so a third call sends nothing.
        await sender.SendAsync(newPhone, sinhala, key);
        Assert.Equal(2, gateway.Requests);
    }

    [Fact]
    public async Task ARetryThatFailsAgain_StaysFailed_AndTheNextRetryStillSends()
    {
        var gateway = new FlakyGateway(failures: 2);
        var sender = Create(gateway);
        var phone = NewPhoneNumber();
        var key = NewDedupKey();

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(phone, "hello", key));
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(phone, "hello", key));
        var failed = Assert.Single(await RowsFor(key));
        Assert.Equal(("Failed", (byte)2), (failed.Status, failed.Attempts));

        await sender.SendAsync(phone, "hello", key);

        var sent = Assert.Single(await RowsFor(key));
        Assert.Equal(("Sent", (byte)3), (sent.Status, sent.Attempts));
        Assert.Equal(3, gateway.Requests);
    }

    [Fact]
    public async Task ASecondRetryWhileTheFirstIsSending_SendsNothing()
    {
        // Taking the row back turns it Queued in the same statement that tests for Failed, so a retry
        // arriving while another is still with the gateway finds nothing to take. The first retry is
        // held inside the gateway to make that overlap certain rather than likely.
        var gateway = new FlakyGateway(failures: 1, holdRequest: 2);
        var sender = Create(gateway);
        var phone = NewPhoneNumber();
        var key = NewDedupKey();
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(phone, "hello", key));

        var first = sender.SendAsync(phone, "hello", key);
        await gateway.Held.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await sender.SendAsync(phone, "hello", key);
        Assert.Equal(2, gateway.Requests);

        gateway.Release.SetResult();
        await first;

        Assert.Equal(2, gateway.Requests);
        Assert.Equal("Sent", Assert.Single(await RowsFor(key)).Status);
    }

    [Fact]
    public async Task RefusesToSendWithoutAToken_AndSaysHowToSetIt()
    {
        var (sender, gateway) = Create(HttpStatusCode.OK, SuccessWithUid, token: null);
        var phone = NewPhoneNumber();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(phone, "hello"));

        Assert.Contains("dotnet user-secrets set \"Sms:TextLkApiToken\"", ex.Message, StringComparison.Ordinal);
        Assert.Null(gateway.Request);
        Assert.Equal("Failed", (await RowFor(phone)).Status);
    }
}

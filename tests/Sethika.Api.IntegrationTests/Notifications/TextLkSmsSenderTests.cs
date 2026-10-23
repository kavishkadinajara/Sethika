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

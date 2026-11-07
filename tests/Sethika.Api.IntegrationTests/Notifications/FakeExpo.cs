using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>
/// Stands where Expo's push API stands, and nowhere else (plan F, F30): behind the HTTP client.
/// Everything on our side of the wire is real, SQL Server included. The answers are the shapes in
/// Expo's documentation.
/// </summary>
internal sealed class FakeExpo : HttpMessageHandler
{
    private const string SendPath = "/--/api/v2/push/send";
    private const string ReceiptsPath = "/--/api/v2/push/getReceipts";

    private readonly ConcurrentQueue<(string Path, string Body)> _requests = new();

    /// <summary>The answer to a send, given the message's token. By default a good ticket.</summary>
    public Func<string, (HttpStatusCode Status, string Json)> OnSend { get; set; } = _ => Ticket(Guid.NewGuid().ToString());

    /// <summary>The answer to a receipts call, given the ids asked about. By default none are ready.</summary>
    public Func<IReadOnlyList<string>, (HttpStatusCode Status, string Json)> OnReceipts { get; set; } = _ => (HttpStatusCode.OK, """{"data":{}}""");

    /// <summary>Every message handed to Expo, in order.</summary>
    public IReadOnlyList<JsonElement> Sent =>
        [.. _requests.Where(r => r.Path == SendPath).Select(r => JsonDocument.Parse(r.Body).RootElement[0])];

    /// <summary>The ids of each receipts call, in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ReceiptCalls =>
        [.. _requests.Where(r => r.Path == ReceiptsPath).Select(r => Ids(r.Body))];

    public static (HttpStatusCode, string) Ticket(string id) =>
        (HttpStatusCode.OK, $$"""{"data":[{"status":"ok","id":"{{id}}"}]}""");

    public static (HttpStatusCode, string) Refusal(string token, string error) =>
        (HttpStatusCode.OK, $$$"""{"data":[{"status":"error","message":"\"{{{token}}}\" is not a registered push notification recipient","details":{"error":"{{{error}}}"}}]}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue((path, body));

        var (status, json) = path switch
        {
            SendPath => OnSend(JsonDocument.Parse(body).RootElement[0].GetProperty("to").GetString()!),
            ReceiptsPath => OnReceipts(Ids(body)),
            _ => throw new InvalidOperationException($"Expo has no {path}."),
        };

        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static List<string> Ids(string body) =>
        [.. JsonDocument.Parse(body).RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetString()!)];
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Sends through the Expo Push Service, which forwards to FCM and APNs (plan F, F4), with
/// <see cref="HttpClient"/> and no SDK.
/// </summary>
/// <remarks>
/// Written to Expo's documentation, and tested with Expo's documented answers behind a stub: no test
/// calls Expo (F30). Each push is its own request. A person has a phone or two, and one phone's
/// refusal then has nothing to do with the other's.
/// </remarks>
public sealed class ExpoPushGateway(HttpClient httpClient) : IPushGateway
{
    /// <summary>
    /// The Android channel. <b>The app must create this exact channel before it asks for a token:</b>
    /// Android shows nothing for a channel the phone does not have.
    /// </summary>
    public const string AndroidChannelId = "bookings";

    /// <summary>
    /// A day. Past that a push about tomorrow's meal is wrong, and the SMS has already told them.
    /// Left out, Expo keeps trying for four weeks.
    /// </summary>
    private const int TimeToLiveSeconds = 86_400;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<PushAnswer> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        ExpoMessage[] body =
        [
            new(
                message.Token,
                message.Title,
                message.Body,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["code"] = message.TemplateCode },
                TimeToLiveSeconds,
                "high",
                "default",
                AndroidChannelId),
        ];

        using var response = await httpClient.PostAsJsonAsync("push/send", body, RequestJson, cancellationToken).ConfigureAwait(false);
        using var document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        // Expo answers an array with an array, except that one message to one recipient may come
        // back as the ticket itself.
        var data = document.RootElement.GetProperty("data");
        var ticket = data.ValueKind == JsonValueKind.Array ? data[0] : data;

        if (IsOk(ticket))
        {
            return ticket.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } ticketId
                ? new PushAnswer(ticketId, Error: null)
                : new PushAnswer(TicketId: null, "Expo gave no ticket id.");
        }

        // The refusal's message quotes the token, and this answer goes on to the log.
        return new PushAnswer(TicketId: null, Reason(ticket)?.Replace(message.Token, "[token]", StringComparison.Ordinal) ?? "Expo gave no reason.");
    }

    public async Task<IReadOnlyDictionary<string, PushAnswer>> GetReceiptsAsync(
        IReadOnlyCollection<string> ticketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        using var response = await httpClient.PostAsJsonAsync("push/getReceipts", new { ids = ticketIds }, RequestJson, cancellationToken).ConfigureAwait(false);
        using var document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        var receipts = new Dictionary<string, PushAnswer>(StringComparer.Ordinal);
        foreach (var receipt in document.RootElement.GetProperty("data").EnumerateObject())
        {
            receipts[receipt.Name] = new PushAnswer(
                TicketId: null,
                IsOk(receipt.Value) ? null : Reason(receipt.Value) ?? "Expo gave no reason.");
        }

        return receipts;
    }

    /// <summary>The body of a whole-request success. A 4xx or 5xx is the whole request refused, so it throws.</summary>
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Expo answered HTTP {(int)response.StatusCode}.", inner: null, response.StatusCode);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool IsOk(JsonElement answer) =>
        answer.TryGetProperty("status", out var status) && status.GetString() == "ok";

    /// <summary>The error code, which every documented refusal has, or else the sentence.</summary>
    private static string? Reason(JsonElement answer)
    {
        if (answer.TryGetProperty("details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("error", out var error)
            && error.GetString() is { Length: > 0 } code)
        {
            return code;
        }

        return answer.TryGetProperty("message", out var message) ? message.GetString() : null;
    }

    private sealed record ExpoMessage(
        string To,
        string? Title,
        string Body,
        Dictionary<string, string> Data,
        int Ttl,
        string Priority,
        string Sound,
        string ChannelId);
}

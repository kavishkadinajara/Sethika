using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Sends through Text.lk, using its OAuth ("v3") API.
/// </summary>
/// <remarks>
/// The v3 API rather than Text.lk's plain HTTP GET API, because the GET API carries the token in
/// the query string, and <see cref="IHttpClientFactory"/> logs every request URI. The token would
/// end up in the log on every message. The v3 API sends it as a bearer header, which is not logged.
///
/// Text.lk's two documentation pages disagree about the response: one shows
/// <c>"status": true</c> with <c>data.sms_id</c>, the other <c>"status": "success"</c> with
/// <c>data.uid</c>. Both are accepted. Anything else is a failure, even with HTTP 200, because
/// recording a refused message as sent is the one mistake the delivery log cannot recover from.
///
/// Like Notify.lk, the gateway wants the recipient as 94XXXXXXXXX without the plus. The stored
/// E.164 value is adjusted here and nowhere else.
/// </remarks>
public sealed class TextLkSmsSender(
    IDbConnectionFactory connectionFactory,
    IOptions<SmsOptions> options,
    HttpClient httpClient) : SmsSenderBase(connectionFactory, options.Value)
{
    protected override async Task<string?> DeliverAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        var token = string.IsNullOrWhiteSpace(Options.TextLkApiToken)
            ? throw new InvalidOperationException(
                "Sms:TextLkApiToken is not configured. Run: dotnet user-secrets set " +
                "\"Sms:TextLkApiToken\" \"...\" --project src/Sethika.Api")
            : Options.TextLkApiToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "sms/send")
        {
            Content = JsonContent.Create(new
            {
                recipient = phoneNumber.TrimStart('+'),
                sender_id = Options.TextLkSenderId,

                // The only documented type. Sinhala and Tamil go as UCS-2 under it, which is what
                // the segment count in SmsSenderBase already assumes.
                type = "plain",
                message,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var reply = Parse(body);

        if (!response.IsSuccessStatusCode || !reply.Accepted)
        {
            // The gateway's own explanation, not the body: the body may echo the message, and for an
            // OTP that is a live credential in the failure column.
            throw new HttpRequestException(string.Create(
                CultureInfo.InvariantCulture,
                $"Text.lk refused the message (HTTP {(int)response.StatusCode}): {reply.Reason ?? "no reason given"}."));
        }

        return reply.MessageId;
    }

    private static Reply Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Reply(false, null, null);
            }

            var accepted = root.TryGetProperty("status", out var status) && status.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            var reason = root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;

            string? messageId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                messageId = Text(data, "uid") ?? Text(data, "sms_id");
            }

            return new Reply(accepted, messageId, reason);
        }
        catch (JsonException)
        {
            // An HTML error page from a proxy, or an empty body: not an acceptance.
            return new Reply(false, null, null);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private sealed record Reply(bool Accepted, string? MessageId, string? Reason);
}

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Sends through Notify.lk.
/// </summary>
/// <remarks>
/// Untested against the live gateway - there is no account yet - so it is written to the documented
/// API and deliberately kept to the one call that matters. It exists now rather than later so that
/// the seam is real: <see cref="ConsoleSmsSender"/> is not the only implementation the design has
/// ever had, which is how an abstraction ends up fitting exactly one caller.
///
/// Notify.lk expects the recipient without a leading plus, so the E.164 value stored in
/// <c>auth.Users</c> is adjusted here rather than stored in the gateway's format - a second storage
/// format would break the unique index that normalisation exists to make meaningful.
/// </remarks>
public sealed class NotifyLkSmsSender(
    IDbConnectionFactory connectionFactory,
    IOptions<SmsOptions> options,
    HttpClient httpClient) : SmsSenderBase(connectionFactory, options.Value)
{
    protected override async Task<string?> DeliverAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        var userId = Require(Options.NotifyLkUserId, nameof(SmsOptions.NotifyLkUserId));
        var apiKey = Require(Options.NotifyLkApiKey, nameof(SmsOptions.NotifyLkApiKey));

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["user_id"] = userId,
            ["api_key"] = apiKey,
            ["sender_id"] = Options.NotifyLkSenderId,
            ["to"] = phoneNumber.TrimStart('+'),
            ["message"] = message,
        };

        var requestUri = "send-sms" + BuildQueryString(query);

        using var response = await httpClient
            .GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<NotifyLkResponse>(cancellationToken).ConfigureAwait(false);

        // The gateway answers 200 with a status field even when it refuses the message, so the HTTP
        // status alone is not the outcome. Treating it as one would record a failed send as sent.
        if (payload is null ||
            !string.Equals(payload.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(
                $"Notify.lk refused the message: {payload?.Status ?? "no response body"}.");
        }

        return payload.Data?.MessageId?.ToString(CultureInfo.InvariantCulture);
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Sms:{name} is not configured. Run: dotnet user-secrets set \"Sms:{name}\" \"...\" " +
                "--project src/Sethika.Api")
            : value;

    /// <summary>Builds an escaped query string. The message contains spaces and may be Sinhala.</summary>
    private static string BuildQueryString(Dictionary<string, string?> parameters)
    {
        var pairs = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");

        return "?" + string.Join('&', pairs);
    }

    private sealed class NotifyLkResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("data")]
        public NotifyLkData? Data { get; init; }
    }

    private sealed class NotifyLkData
    {
        [JsonPropertyName("message_id")]
        public long? MessageId { get; init; }
    }
}

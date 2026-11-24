namespace Sethika.Infrastructure.Notifications;

/// <summary>Which push implementation to use. Section <c>Push</c>.</summary>
public sealed class PushOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Push";

    /// <summary>Writes the push to the log instead of sending it. Development only.</summary>
    public const string ConsoleProvider = "Console";

    /// <summary>Sends through the Expo Push Service (plan F, F4).</summary>
    public const string ExpoProvider = "Expo";

    /// <summary>
    /// Which gateway to register: <see cref="ConsoleProvider"/> or <see cref="ExpoProvider"/>.
    /// </summary>
    /// <remarks>
    /// As with <c>Sms:Provider</c>, the host refuses to start with the console gateway outside
    /// Development: a forgotten setting would otherwise mean nobody gets a push and nothing says why.
    /// </remarks>
    public string Provider { get; set; } = ConsoleProvider;

    /// <summary>Expo's push API. Needs no key unless the project turns on enhanced push security.</summary>
    public string ExpoBaseUrl { get; set; } = "https://exp.host/--/api/v2/";

    /// <summary>
    /// How long a call to Expo may take. Short, because a send runs inside the outbox's batch
    /// transaction, and a push is never worth holding up the texts behind it (F29).
    /// </summary>
    public int ExpoTimeoutSeconds { get; set; } = 10;

    /// <summary>Tickets asked about in one call. Expo takes at most 1000.</summary>
    public int ReceiptBatchSize { get; set; } = 1000;
}

namespace Sethika.Infrastructure.Notifications;

/// <summary>Which SMS implementation to use, and what a message costs.</summary>
public sealed class SmsOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Sms";

    /// <summary>Writes the message to the log instead of sending it. Development only.</summary>
    public const string ConsoleProvider = "Console";

    /// <summary>Sends through Notify.lk.</summary>
    public const string NotifyLkProvider = "NotifyLk";

    /// <summary>
    /// Which sender to register. <see cref="ConsoleProvider"/> or <see cref="NotifyLkProvider"/>.
    /// </summary>
    /// <remarks>
    /// The host refuses to start with <see cref="ConsoleProvider"/> outside Development. A silent
    /// console sender in production means nobody receives an OTP and sign-in simply appears broken,
    /// with nothing in the logs to say why - the kind of failure that costs hours to find.
    /// </remarks>
    public string Provider { get; set; } = ConsoleProvider;

    /// <summary>Characters per billable GSM-7 segment.</summary>
    /// <remarks>
    /// A longer message is split by the carrier and billed per part, so this is what makes
    /// <c>SmsSegments</c> and <c>CostLkr</c> meaningful rather than decorative.
    /// </remarks>
    public int SegmentLength { get; set; } = 160;

    /// <summary>Characters per segment once the message contains non-GSM-7 characters.</summary>
    /// <remarks>
    /// Sinhala and Tamil force UCS-2, which drops the segment from 160 characters to 70. A Sinhala
    /// OTP message therefore costs more to send than the same message in English, and the cost
    /// column has to reflect that or the per-plan SMS quota understates real usage for exactly the
    /// users the platform is for.
    /// </remarks>
    public int UnicodeSegmentLength { get; set; } = 70;

    /// <summary>Price of one segment in rupees, used to fill <c>CostLkr</c>.</summary>
    public decimal CostPerSegmentLkr { get; set; } = 0.50m;

    /// <summary>Notify.lk API base address.</summary>
    public string NotifyLkBaseUrl { get; set; } = "https://app.notify.lk/api/v1/";

    /// <summary>Notify.lk user id. Comes from User Secrets, never appsettings.</summary>
    public string? NotifyLkUserId { get; set; }

    /// <summary>Notify.lk API key. Comes from User Secrets, never appsettings.</summary>
    public string? NotifyLkApiKey { get; set; }

    /// <summary>
    /// Registered sender id shown to the recipient.
    /// </summary>
    /// <remarks>
    /// Sri Lankan gateways require this to be registered and approved before it can be used, which
    /// takes days. Until then only the gateway's own demo sender works.
    /// </remarks>
    public string NotifyLkSenderId { get; set; } = "SethikaLK";
}

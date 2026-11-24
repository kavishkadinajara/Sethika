namespace Sethika.Infrastructure.Security;

/// <summary>The <c>Payments</c> configuration section (plan 09, M10, M21).</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>The key new values are sealed with; it must be listed in <see cref="Keys"/>.</summary>
    public string? CurrentKeyId { get; set; }

    /// <summary>Every key that may open a stored value, by id: 32 bytes each, base64.</summary>
    public Dictionary<string, string> Keys { get; set; } = [];

    public PayHereOptions PayHere { get; set; } = new();
}

/// <summary>The <c>Payments:PayHere</c> section. None of this is secret; each institution's secret is its own.</summary>
public sealed class PayHereOptions
{
    /// <summary>PayHere's sandbox checkout when true, the live one when false.</summary>
    public bool Sandbox { get; set; } = true;

    /// <summary>The API's public address, for <c>notify_url</c>.</summary>
    public string? ApiPublicUrl { get; set; }

    /// <summary>The web app's public address, for the return page.</summary>
    public string? WebPublicUrl { get; set; }
}

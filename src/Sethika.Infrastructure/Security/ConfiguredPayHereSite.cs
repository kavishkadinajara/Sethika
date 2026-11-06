using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Security;

/// <summary><see cref="IPayHereSite"/> from <see cref="PayHereOptions"/>.</summary>
/// <remarks>
/// <see cref="Create"/> checks the addresses at start-up. Outside Development they must be HTTPS, because
/// PayHere posts the notification to one of them and sends the donor back to the other.
/// </remarks>
public sealed class ConfiguredPayHereSite : IPayHereSite
{
    private const string SandboxCheckout = "https://sandbox.payhere.lk/pay/checkout";

    // PayHere's page: without the www, a live request may fail with PH-0022.
    private const string LiveCheckout = "https://www.payhere.lk/pay/checkout";

    private const string NotifyPath = "/api/payments/payhere/notify";

    /// <summary>English is unprefixed (web/i18n/routing.ts).</summary>
    private const string DefaultLanguage = "en";

    private readonly string _web;

    private ConfiguredPayHereSite(bool sandbox, string api, string web)
    {
        CheckoutUrl = sandbox ? SandboxCheckout : LiveCheckout;
        NotifyUrl = api + NotifyPath;
        _web = web;
    }

    public string CheckoutUrl { get; }

    public string NotifyUrl { get; }

    /// <exception cref="InvalidOperationException">An address is missing or wrong, and says which.</exception>
    public static ConfiguredPayHereSite Create(PayHereOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ConfiguredPayHereSite(
            options.Sandbox,
            Address(options.ApiPublicUrl, "Payments:PayHere:ApiPublicUrl", isDevelopment),
            Address(options.WebPublicUrl, "Payments:PayHere:WebPublicUrl", isDevelopment));
    }

    public string ReturnUrl(Guid orderId, string language) =>
        language == DefaultLanguage ? $"{_web}/giving/return/{orderId}" : $"{_web}/{language}/giving/return/{orderId}";

    public string CancelUrl(Guid orderId, string language) => ReturnUrl(orderId, language) + "?left=1";

    private static string Address(string? value, string key, bool isDevelopment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && (!isDevelopment || uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException($"{key} is not set to an absolute https:// address, so PayHere could not reach Sethika.");
        }

        return value!.TrimEnd('/');
    }
}

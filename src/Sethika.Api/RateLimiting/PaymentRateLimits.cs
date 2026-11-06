using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>The limits on online giving (plan 09, D.3).</summary>
internal static class PaymentRateLimits
{
    /// <summary>
    /// Orders made by one account (M8). Each writes a row and signs a form, and a donor who changes their
    /// mind a few times is still far inside it; a loop filling the table is not.
    /// </summary>
    public static readonly RateLimitRule GivePerUser = new("payment-give-user", 30, TimeSpan.FromHours(1));

    /// <summary>
    /// Notifications from one address. Loose on purpose: PayHere posts from few addresses and a refused
    /// notification is a gift with no receipt. It only bounds a script making the API check signatures.
    /// </summary>
    public static readonly RateLimitRule NotifyPerClient = new("payhere-notify-client", 3000, TimeSpan.FromMinutes(15));
}

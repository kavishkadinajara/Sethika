namespace Sethika.Application.Abstractions;

/// <summary>Where PayHere and Sethika meet, from the platform's configuration (plan 09, M10).</summary>
public interface IPayHereSite
{
    /// <summary>PayHere's sandbox or live checkout, by the platform's <c>Payments:PayHere:Sandbox</c>.</summary>
    string CheckoutUrl { get; }

    /// <summary>Where PayHere posts its notification. It must be reachable from the internet.</summary>
    string NotifyUrl { get; }

    /// <summary>The return page for an order, in the donor's language.</summary>
    string ReturnUrl(Guid orderId, string language);

    /// <summary>The same page, saying the donor left before paying (<c>cancel_url</c>).</summary>
    string CancelUrl(Guid orderId, string language);
}

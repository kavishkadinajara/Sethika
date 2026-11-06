using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sethika.Domain.Giving;

/// <summary>
/// PayHere's two signatures (plan 09, M8, M9), as its Checkout API documents them: both are the upper-case
/// MD5 of the fields joined together, ending with the upper-case MD5 of the merchant secret.
/// </summary>
/// <remarks>
/// MD5 is PayHere's choice, not ours, and it is the only way to speak to the gateway. It is used for
/// nothing else here: the secret itself is stored with AES-256-GCM (M21).
/// </remarks>
public static class PayHereSignature
{
    /// <summary>An amount as the checkout hash signs it: <c>1000.00</c>, no separators, in any culture.</summary>
    public static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The <c>hash</c> field of the checkout form.</summary>
    public static string Checkout(string merchantId, string orderId, decimal amount, string currency, string secret) =>
        Md5(merchantId + orderId + Amount(amount) + currency + Md5(secret));

    /// <summary>
    /// The <c>md5sig</c> a notification must carry. <paramref name="amount"/> and
    /// <paramref name="statusCode"/> are the exact text PayHere sent, so nothing is reformatted before checking.
    /// </summary>
    public static string Notification(string merchantId, string orderId, string amount, string currency, string statusCode, string secret) =>
        Md5(merchantId + orderId + amount + currency + statusCode + Md5(secret));

    /// <summary>Compares in constant time, so a forger learns nothing from how long a refusal takes.</summary>
    public static bool Matches(string expected, string? received) =>
        received is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(received));

#pragma warning disable CA5351 // PayHere's API requires MD5; see the class remarks.
    private static string Md5(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));
#pragma warning restore CA5351
}

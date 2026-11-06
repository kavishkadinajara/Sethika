namespace Sethika.Domain.Giving;

/// <summary>
/// An online gift through PayHere, from the order to PayHere's answer (plan 09, M8, M9). Maps
/// <c>giving.OnlinePayments</c>.
/// </summary>
/// <remarks>
/// A payment that has not been confirmed is not a donation: it has no receipt number. Only PayHere's
/// signed notification makes it <see cref="OnlinePaymentStatus.Paid"/>, and that writes the donation in
/// the same transaction. The status moves by conditional UPDATEs (<c>OnlinePaymentHandler</c>), never
/// through a tracked row, so a repeated notification finds nothing left to do.
/// </remarks>
public class OnlinePayment
{
    /// <summary>The least a donor may give online, in LKR (<c>CK_OnlinePay_Amount</c>).</summary>
    public const decimal MinAmount = 100m;

    /// <summary>The most a donor may give online at once, in LKR (<c>CK_OnlinePay_Amount</c>).</summary>
    public const decimal MaxAmount = 1_000_000m;

    /// <summary>The currency every order is made in.</summary>
    public const string Currency = "LKR";

    /// <summary><c>PayHerePaymentId</c> is <c>VARCHAR(30)</c>.</summary>
    public const int MaxPaymentId = 30;

    public long Id { get; set; }

    /// <summary>What PayHere is given and sends back, and the address of the return page (v7, set in C#).</summary>
    public Guid OrderId { get; set; }

    public int InstitutionId { get; set; }
    public Guid DonorUserId { get; set; }

    /// <summary>The institution's merchant this order was made for (M10): a notification must name the same one.</summary>
    public string MerchantId { get; set; } = null!;

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = Currency;
    public int? NeedId { get; set; }

    /// <summary>One of <see cref="OnlinePaymentStatus"/>.</summary>
    public string Status { get; set; } = OnlinePaymentStatus.Pending;

    public string? PayHerePaymentId { get; set; }
    public short? PayHereStatusCode { get; set; }

    /// <summary>The gift this order became; set exactly when it is <see cref="OnlinePaymentStatus.Paid"/> (<c>CK_OnlinePay_Paid</c>).</summary>
    public long? DonationId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Where an order stands, as <c>CK_OnlinePay_Status</c> spells it.</summary>
public static class OnlinePaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>PayHere's <c>status_code</c> values.</summary>
public static class PayHereStatus
{
    public const short Success = 2;
    public const short Pending = 0;
    public const short Cancelled = -1;
    public const short Failed = -2;
    public const short ChargedBack = -3;
}

namespace Sethika.Domain.Giving;

/// <summary>Where a pledge stands, as stored.</summary>
public static class PledgeStatus
{
    public const string Pledged = "Pledged";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// A donor's promise of part of a need (plan 09, M13). Maps <c>giving.Pledges</c>.
/// </summary>
/// <remarks>
/// Settled once: delivered (M15) or cancelled, and then it stays as it is. Voiding the receipt of a
/// delivered pledge leaves it delivered (M30), because the delivery happened; the void corrects the count.
/// </remarks>
public class Pledge
{
    /// <summary>At most this many open pledges per donor per need (M13).</summary>
    public const int MaxOpenPerDonor = 5;

    /// <summary><c>CancelReason</c> is <c>NVARCHAR(300)</c>.</summary>
    public const int MaxReason = 300;

    public int Id { get; set; }

    /// <summary>The pledge's address in URLs (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public int NeedId { get; set; }
    public Guid DonorUserId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }

    /// <summary><see cref="PledgeStatus"/>.</summary>
    public string Status { get; private set; } = PledgeStatus.Pledged;

    public Guid? ReceivedBy { get; private set; }
    public DateTime? ReceivedAt { get; private set; }
    public decimal? ReceivedQuantity { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Marks it delivered with what actually came: more than 0, at most what was promised (M15).</summary>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public string? Receive(Guid by, decimal quantity, DateTime utcNow)
    {
        if (Status != PledgeStatus.Pledged)
        {
            return NeedErrors.PledgeNotOpen;
        }

        if (quantity <= 0 || quantity > Quantity || decimal.Round(quantity, 2) != quantity)
        {
            return NeedErrors.InvalidQuantity;
        }

        Status = PledgeStatus.Delivered;
        ReceivedBy = by;
        ReceivedAt = utcNow;
        ReceivedQuantity = quantity;
        return null;
    }

    /// <summary>Cancels it, by its donor or by staff. Staff must say why (M13): the donor reads it.</summary>
    /// <param name="by">Who cancels it.</param>
    /// <param name="reason">Why; at most <see cref="MaxReason"/> characters.</param>
    /// <param name="reasonRequired">Staff's cancel; a donor owes nobody a reason.</param>
    /// <param name="utcNow">The moment.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public string? Cancel(Guid by, string? reason, bool reasonRequired, DateTime utcNow)
    {
        if (Status != PledgeStatus.Pledged)
        {
            return NeedErrors.PledgeNotOpen;
        }

        var tidy = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (tidy is { Length: > MaxReason } || (reasonRequired && tidy is null))
        {
            return NeedErrors.InvalidReason;
        }

        Status = PledgeStatus.Cancelled;
        CancelledBy = by;
        CancelledAt = utcNow;
        CancelReason = tidy;
        return null;
    }
}

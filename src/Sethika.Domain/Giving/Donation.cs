namespace Sethika.Domain.Giving;

/// <summary>
/// A gift an institution received: money or items (plan 09, M1). Maps <c>giving.Donations</c>.
/// </summary>
/// <remarks>
/// Never edited and never deleted (CLAUDE.md): a mistake is corrected by voiding the gift and recording
/// it again. Voiding is a second person's act (<see cref="Void"/>, M6). The database refuses anything
/// else (<c>trg_Donations_Immutable</c>, M7), so this class offers no setter-style change after it is
/// saved; the properties are settable only because EF Core materialises them.
/// </remarks>
public class Donation
{
    public const string Money = "Money";
    public const string InKind = "InKind";

    /// <summary><c>VoidReason</c> and <c>Note</c> are <c>NVARCHAR(300)</c>.</summary>
    public const int MaxReason = 300;

    public long Id { get; set; }

    /// <summary>The receipt's address in URLs and from the donor's side (v7, set in C#).</summary>
    public Guid PublicId { get; set; }

    /// <summary>From the institution's own <c>RECEIPT</c> series (M2): unique inside the institution only.</summary>
    public string ReceiptNo { get; set; } = null!;

    public int InstitutionId { get; set; }

    /// <summary>The account that may read the receipt: a donor book row's link, or the donor online.</summary>
    public Guid? DonorUserId { get; set; }

    public int? InstitutionDonorId { get; set; }

    /// <summary>Who gave, as a snapshot (M3).</summary>
    public string DonorName { get; set; } = null!;

    public string? DonorPhone { get; set; }

    /// <summary><see cref="Money"/> or <see cref="InKind"/>.</summary>
    public string Kind { get; set; } = null!;

    public decimal? Amount { get; set; }
    public string CurrencyCode { get; set; } = "LKR";
    public int? PaymentMethodLookupId { get; set; }
    public string? ItemDescription { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? EstimatedValue { get; set; }
    public int? NeedId { get; set; }

    /// <summary>The pledge this gift delivered (M15); null for a gift that came without one.</summary>
    public int? PledgeId { get; set; }

    public string? Note { get; set; }

    /// <summary>When it was given (UTC); a past day is stored at noon in Colombo (M23).</summary>
    public DateTime DonatedAt { get; set; }

    public Guid RecordedBy { get; set; }
    public DateTime RecordedAt { get; set; }
    public bool IsVoided { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? VoidedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Voids the gift: it stays in every list, marked, and leaves every total (M6).
    /// </summary>
    /// <param name="by">Who voids it; never the person who recorded it (maker-checker).</param>
    /// <param name="reason">Why; required, at most <see cref="MaxReason"/> characters.</param>
    /// <returns>The refusal, or <see langword="null"/> when it is voided.</returns>
    public string? Void(Guid by, string? reason, DateTime utcNow)
    {
        if (IsVoided)
        {
            return DonationErrors.AlreadyVoid;
        }

        if (by == RecordedBy)
        {
            return DonationErrors.VoidOwnRecord;
        }

        var tidy = reason?.Trim();
        if (string.IsNullOrEmpty(tidy) || tidy.Length > MaxReason)
        {
            return DonationErrors.InvalidReason;
        }

        IsVoided = true;
        VoidedBy = by;
        VoidReason = tidy;
        VoidedAt = utcNow;
        return null;
    }
}

/// <summary>The donation refusals, as the API names them.</summary>
public static class DonationErrors
{
    /// <summary>The gift is voided already; a void is never repeated or undone.</summary>
    public const string AlreadyVoid = "already_void";

    /// <summary>The person who recorded a gift cannot void it (M6): another must.</summary>
    public const string VoidOwnRecord = "void_own_record";

    /// <summary>A void needs a reason of at most 300 characters.</summary>
    public const string InvalidReason = "invalid_reason";
}

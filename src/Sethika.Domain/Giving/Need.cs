namespace Sethika.Domain.Giving;

/// <summary>Where a need stands (plan 09, M11), as stored.</summary>
public static class NeedStatus
{
    public const string Draft = "Draft";
    public const string Open = "Open";
    public const string Fulfilled = "Fulfilled";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

/// <summary>How pressing a need is, as stored.</summary>
public static class NeedPriority
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string Urgent = "Urgent";

    public static readonly IReadOnlyList<string> All = [Low, Normal, Urgent];
}

/// <summary>
/// Something an institution asks for on the needs board (plan 09, M11). Maps <c>giving.Needs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two counters tell its progress, and one rule holds them together (M29): <see cref="QuantityPledged"/>
/// is everything spoken for, so <see cref="QuantityReceived"/> plus what open pledges still promise. A gift
/// that arrives without a pledge therefore moves both. What is left to pledge is always
/// <see cref="QuantityNeeded"/> less <see cref="QuantityPledged"/>, and <see cref="OpenPledged"/> is what
/// open pledges hold, without counting them.
/// </para>
/// <para>
/// The counters change only by conditional UPDATE, never through this class (<c>NeedCounters</c>, M13):
/// a tracked copy would write back what it read. Its methods judge a staff member's change to the rest.
/// </para>
/// </remarks>
public class Need
{
    /// <summary><c>Title</c> is <c>NVARCHAR(150)</c>.</summary>
    public const int MaxTitle = 150;

    /// <summary><c>Description</c> is <c>NVARCHAR(1000)</c>.</summary>
    public const int MaxDescription = 1000;

    public int Id { get; set; }

    /// <summary>The need's address in URLs.</summary>
    public Guid PublicId { get; set; }

    public int InstitutionId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int CategoryLookupId { get; set; }
    public int UnitLookupId { get; set; }
    public decimal QuantityNeeded { get; set; }
    public decimal QuantityPledged { get; private set; }
    public decimal QuantityReceived { get; private set; }

    /// <summary><see cref="NeedPriority"/>.</summary>
    public string Priority { get; set; } = NeedPriority.Normal;

    public DateOnly? Deadline { get; set; }

    /// <summary><see cref="NeedStatus"/>.</summary>
    public string Status { get; private set; } = NeedStatus.Draft;

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>When it went on the board; <see langword="null"/> for a draft (M37).</summary>
    public DateTime? OpenedAt { get; private set; }

    /// <summary>When it left the board, fulfilled, closed or cancelled; <see langword="null"/> while it stands (M37).</summary>
    public DateTime? EndedAt { get; private set; }

    /// <summary>What open pledges still promise.</summary>
    public decimal OpenPledged => QuantityPledged - QuantityReceived;

    /// <summary>Whether its details may still change: a settled need is history.</summary>
    public bool IsEditable => Status is NeedStatus.Draft or NeedStatus.Open;

    /// <summary>
    /// A new quantity (M11): never below what is spoken for. Brought down to exactly what has arrived,
    /// the need has what it asked for, so it is fulfilled.
    /// </summary>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public string? ChangeQuantity(decimal quantity, DateTime now)
    {
        if (quantity < QuantityPledged)
        {
            return NeedErrors.QuantityBelowPledged;
        }

        QuantityNeeded = quantity;
        if (Status == NeedStatus.Open && QuantityReceived >= quantity)
        {
            Status = NeedStatus.Fulfilled;
            EndedAt = now;
        }

        return null;
    }

    /// <summary>Puts a draft on the board.</summary>
    public string? Publish(DateTime now)
    {
        if (Status != NeedStatus.Draft)
        {
            return NeedErrors.WrongStatus;
        }

        Status = NeedStatus.Open;
        OpenedAt = now;
        return null;
    }

    /// <summary>
    /// Stops it taking more, keeping what came. Open pledges must be settled first (M29): each is a
    /// person's promise, delivered or cancelled by name, never swept away.
    /// </summary>
    public string? Close(DateTime now)
    {
        if (Status != NeedStatus.Open)
        {
            return NeedErrors.WrongStatus;
        }

        if (OpenPledged > 0)
        {
            return NeedErrors.OpenPledges;
        }

        Status = NeedStatus.Closed;
        EndedAt = now;
        return null;
    }

    /// <summary>Withdraws a need nothing came for; one that received gifts is closed instead, so those stay counted.</summary>
    public string? Cancel(DateTime now)
    {
        if (!IsEditable)
        {
            return NeedErrors.WrongStatus;
        }

        if (OpenPledged > 0)
        {
            return NeedErrors.OpenPledges;
        }

        if (QuantityReceived > 0)
        {
            return NeedErrors.HasGifts;
        }

        Status = NeedStatus.Cancelled;
        EndedAt = now;
        return null;
    }
}

/// <summary>The needs and pledges refusals, as the API names them.</summary>
public static class NeedErrors
{
    /// <summary>No such need here, or not one the caller may see.</summary>
    public const string NotFound = "need_not_found";

    /// <summary>No such pledge here, or not the caller's.</summary>
    public const string PledgeNotFound = "pledge_not_found";

    /// <summary>The need has room for less than that: everything else is spoken for.</summary>
    public const string Full = "need_full";

    /// <summary>The need no longer takes anything.</summary>
    public const string Closed = "need_closed";

    /// <summary>The need's status does not allow this step.</summary>
    public const string WrongStatus = "need_wrong_status";

    /// <summary>A quantity below what is pledged or received (M11).</summary>
    public const string QuantityBelowPledged = "quantity_below_pledged";

    /// <summary>Open pledges must be delivered or cancelled first.</summary>
    public const string OpenPledges = "open_pledges";

    /// <summary>Gifts came for it, so it is closed rather than cancelled.</summary>
    public const string HasGifts = "need_has_gifts";

    /// <summary>The pledge is already delivered or cancelled.</summary>
    public const string PledgeNotOpen = "pledge_not_open";

    /// <summary>At most 5 open pledges per donor per need (M13).</summary>
    public const string TooManyPledges = "too_many_pledges";

    /// <summary>A quantity that is not above 0, or more than was pledged.</summary>
    public const string InvalidQuantity = "invalid_quantity";

    /// <summary>A reason longer than the column, or missing where it is required.</summary>
    public const string InvalidReason = "invalid_reason";
}

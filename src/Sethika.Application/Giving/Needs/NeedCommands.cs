namespace Sethika.Application.Giving.Needs;

/// <summary>A need as the add and edit sheets send it (plan 09, M11).</summary>
/// <param name="Category">A <c>NEED_CATEGORY</c> code.</param>
/// <param name="Unit">A <c>UNIT</c> code. Fixed once anything is spoken for: a pledge of 10 boxes is not 10 kg.</param>
/// <param name="Quantity">More than 0, two decimals at most; on an edit, never below what is spoken for.</param>
/// <param name="Priority"><c>Low</c>, <c>Normal</c> or <c>Urgent</c>, as stored.</param>
/// <param name="Deadline">Optional; a new deadline is not in the past.</param>
/// <param name="Version">On an edit: the <c>RowVersion</c> as the list read it.</param>
public sealed record NeedInput(
    string? Title,
    string? Description,
    string? Category,
    string? Unit,
    decimal? Quantity,
    string? Priority,
    DateOnly? Deadline,
    byte[]? Version = null);

/// <summary>A step in a need's life: the version the list read.</summary>
public sealed record NeedStepInput(byte[]? Version);

/// <summary>A donor's pledge (M13).</summary>
/// <param name="Quantity">More than 0, two decimals at most, within what is left.</param>
/// <param name="ExpectedDate">When they expect to bring it, optional: today or later, within a year.</param>
public sealed record PledgeInput(decimal? Quantity, DateOnly? ExpectedDate);

/// <summary>A delivery as staff mark it (M15).</summary>
/// <param name="Quantity">What actually came: more than 0, at most what was pledged.</param>
/// <param name="Version">The pledge's <c>RowVersion</c> as the list read it.</param>
public sealed record ReceiveInput(decimal? Quantity, byte[]? Version);

/// <summary>Staff cancelling a pledge (M13).</summary>
/// <param name="Reason">Required; the donor reads it in My giving.</param>
/// <param name="Version">The pledge's <c>RowVersion</c> as the list read it.</param>
public sealed record CancelPledgeInput(string? Reason, byte[]? Version);

/// <summary>The outcome of a need or pledge command.</summary>
/// <param name="Error">An API error code; <see langword="null"/> on success.</param>
/// <param name="Fields">For <see cref="InvalidDetails"/>: the fields to fix, as the API names them.</param>
/// <param name="PublicId">The need or pledge.</param>
/// <param name="ReceiptNo">A delivery's receipt.</param>
/// <param name="ReceiptPublicId">A delivery's receipt's address.</param>
public sealed record NeedResult(string? Error, IReadOnlyList<string> Fields, Guid? PublicId, string? ReceiptNo = null, Guid? ReceiptPublicId = null)
{
    /// <summary>Some fields are missing or malformed; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>Someone changed it since it was read; read it again.</summary>
    public const string Stale = "stale";

    /// <summary>The account has no confirmed phone: a pledge is a promise an institution may need to follow up.</summary>
    public const string PhoneRequired = "phone_required";

    public bool Succeeded => Error is null;

    internal static NeedResult For(Guid publicId) => new(null, [], publicId);

    internal static NeedResult Receipt(Guid publicId, string receiptNo, Guid receipt) => new(null, [], publicId, receiptNo, receipt);

    internal static NeedResult Failed(string error) => new(error, [], null);

    internal static NeedResult Invalid(params string[] fields) => new(InvalidDetails, fields, null);
}

namespace Sethika.Application.Abstractions;

/// <summary>A need on the institution's own screen (plan 09, §5): every status.</summary>
/// <param name="Category">The <c>NEED_CATEGORY</c> code.</param>
/// <param name="Unit">The <c>UNIT</c> code.</param>
/// <param name="OpenPledges">How many pledges are waiting to be delivered.</param>
/// <param name="Version">The <c>RowVersion</c> a change sends back.</param>
public sealed record NeedListItem(
    Guid PublicId,
    string Title,
    string? Description,
    string Category,
    string Unit,
    decimal QuantityNeeded,
    decimal QuantityPledged,
    decimal QuantityReceived,
    string Priority,
    DateOnly? Deadline,
    string Status,
    int OpenPledges,
    byte[] Version);

/// <summary>A pledge as the institution sees it: who, how many, and how it ended.</summary>
/// <param name="ReceiptPublicId">The delivery's receipt.</param>
/// <param name="Version">The <c>RowVersion</c> a receive or cancel sends back.</param>
public sealed record NeedPledgeItem(
    Guid PublicId,
    string DonorName,
    string? DonorPhone,
    decimal Quantity,
    DateOnly? ExpectedDate,
    string Status,
    decimal? ReceivedQuantity,
    string? CancelReason,
    DateTime CreatedAt,
    Guid? ReceiptPublicId,
    string? ReceiptNo,
    byte[] Version);

/// <summary>A need on the public board (M12): nothing about who pledged.</summary>
public sealed record PublicNeed(
    Guid PublicId,
    string Title,
    string? Description,
    string Category,
    string Unit,
    decimal QuantityNeeded,
    decimal QuantityPledged,
    decimal QuantityReceived,
    string Priority,
    DateOnly? Deadline,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string InstitutionSlug,
    string City,
    string District,
    string? DistrictSi,
    string? DistrictTa);

/// <summary>What narrows the board. All optional; a code nobody has finds nothing.</summary>
/// <param name="Institution">One institution's slug, for its own page: listed or not, as the page itself is.</param>
public sealed record NeedBoardFilter(string? District, string? Type, string? Category, string? Institution);

/// <summary>A pledge in the donor's My giving.</summary>
public sealed record MyPledgeItem(
    Guid PublicId,
    string NeedTitle,
    string Unit,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string InstitutionSlug,
    decimal Quantity,
    DateOnly? ExpectedDate,
    string Status,
    decimal? ReceivedQuantity,
    string? CancelReason,
    DateTime CreatedAt,
    Guid? ReceiptPublicId,
    string? ReceiptNo);

/// <summary>The lists a need is written with (M20), in the three languages.</summary>
public sealed record NeedLookups(IReadOnlyList<LookupItem> Categories, IReadOnlyList<LookupItem> Units);

/// <summary>Dapper reads for needs and pledges (plan 09, D.2).</summary>
/// <remarks>
/// <c>giving.Needs</c> has no read filter, since the board is public, so a draft is kept off it by these
/// queries and not by Row-Level Security. Pledges are filtered by <c>sec.fn_TenantOrOwner</c>, and each query
/// names the institution or the account as well.
/// </remarks>
public interface INeedQueries
{
    /// <summary>The institution's needs, open ones first.</summary>
    Task<IReadOnlyList<NeedListItem>> ListNeedsAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The pledges on one of the institution's needs, newest first; <see langword="null"/> when it is not theirs.</summary>
    Task<IReadOnlyList<NeedPledgeItem>?> ListPledgesAsync(int institutionId, Guid needId, CancellationToken cancellationToken = default);

    /// <summary>The open needs the public may see: urgent first, then the nearest deadline.</summary>
    Task<IReadOnlyList<PublicNeed>> ListBoardAsync(NeedBoardFilter filter, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>The donor's pledges, newest first.</summary>
    Task<IReadOnlyList<MyPledgeItem>> ListMyPledgesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The categories and units, in order.</summary>
    Task<NeedLookups> GetLookupsAsync(CancellationToken cancellationToken = default);
}

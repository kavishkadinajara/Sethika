namespace Sethika.Application.Abstractions;

/// <summary>
/// The merchant settings as the screen reads them (plan 09, M10). There is no secret here, in any form:
/// only that one is set, when, and by whom.
/// </summary>
/// <param name="Version">The <c>RowVersion</c> a save sends back.</param>
public sealed record MerchantSettings(
    string MerchantId,
    bool IsEnabled,
    DateTime UpdatedAt,
    string UpdatedByName,
    byte[] Version);

/// <summary>An order as the return page shows it: only what the API knows (M9).</summary>
/// <param name="Status"><c>Pending</c>, <c>Paid</c>, <c>Failed</c> or <c>Cancelled</c>.</param>
/// <param name="ReceiptPublicId">The gift's receipt, once paid.</param>
public sealed record MyPayment(
    Guid OrderId,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string InstitutionSlug,
    Guid? ReceiptPublicId,
    string? ReceiptNo);

/// <summary>Dapper reads for online giving. Row-Level Security applies, and each query names its owner as well.</summary>
public interface IPaymentQueries
{
    /// <summary>The institution's merchant settings, or <see langword="null"/> when none are saved.</summary>
    Task<MerchantSettings?> GetMerchantAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>One of the donor's own orders, or <see langword="null"/>.</summary>
    Task<MyPayment?> GetMyPaymentAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);
}

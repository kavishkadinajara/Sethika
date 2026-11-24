namespace Sethika.Application.Abstractions;

/// <summary>A gift in the institution's list (plan 09, §5): voided ones too, marked (M6).</summary>
/// <param name="Kind"><c>Money</c> or <c>InKind</c>.</param>
/// <param name="Method">The payment method's code, for money.</param>
/// <param name="FromDonorBook">Recorded against a donor book row, not a walk-in's typed name.</param>
/// <param name="RecordedByMe">The caller recorded it, so may not void it (maker-checker).</param>
/// <param name="Version">The <c>RowVersion</c> a void sends back.</param>
public sealed record DonationListItem(
    Guid PublicId,
    string ReceiptNo,
    string Kind,
    decimal? Amount,
    string? Method,
    string? Items,
    decimal? Quantity,
    DateTime DonatedAt,
    string DonorName,
    bool FromDonorBook,
    string RecordedByName,
    bool RecordedByMe,
    bool IsVoided,
    DateTime? VoidedAt,
    byte[] Version);

/// <summary>One printable receipt (M4): what the institution and the donor both see.</summary>
public sealed record ReceiptView(
    Guid PublicId,
    string ReceiptNo,
    string Kind,
    decimal? Amount,
    string CurrencyCode,
    string? Method,
    string? Items,
    decimal? Quantity,
    decimal? EstimatedValue,
    string? Note,
    DateTime DonatedAt,
    DateTime RecordedAt,
    string DonorName,
    string RecordedByName,
    ReceiptInstitution Institution,
    bool IsVoided,
    string? VoidReason,
    DateTime? VoidedAt,
    string? VoidedByName);

/// <summary>The institution as a receipt names it.</summary>
public sealed record ReceiptInstitution(
    string Name,
    string? NameSi,
    string? NameTa,
    string Slug,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Phone);

/// <summary>A gift in the donor's My giving (M18): theirs, in any institution.</summary>
public sealed record MyDonationItem(
    Guid PublicId,
    string ReceiptNo,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string InstitutionSlug,
    string Kind,
    decimal? Amount,
    string? Items,
    decimal? Quantity,
    DateTime DonatedAt,
    bool IsVoided);

/// <summary>A value of a seeded list, in the three languages (M20).</summary>
public sealed record LookupItem(string Code, string Name, string? NameSi, string? NameTa);

/// <summary>What the receipt text (M5) needs: the gift, the institution, and who may be texted.</summary>
/// <param name="DonorUserId">The account to text, as any account is; null for a donor book row without one.</param>
/// <param name="DonorBookPhone">The donor book row's phone, texted only with <paramref name="TextConsent"/>.</param>
/// <param name="Language">The account's language, else the donor book row's: the institution's name is sent in it.</param>
public sealed record DonationTextDetails(
    int InstitutionId,
    string ReceiptNo,
    string Kind,
    decimal? Amount,
    bool IsVoided,
    Guid? DonorUserId,
    string? DonorBookPhone,
    string? Language,
    bool TextConsent,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa);

/// <summary>Dapper reads for giving (plan 09).</summary>
/// <remarks>
/// Row-Level Security keeps donations to their institution and their donor's account
/// (<c>sec.fn_TenantOrOwner</c>); each query names the institution or the account as well, so a platform
/// connection, which sees every row, still reads only what the caller may.
/// </remarks>
public interface IGivingQueries
{
    /// <summary>The institution's gifts given in the Colombo month starting <paramref name="month"/>, newest first.</summary>
    Task<IReadOnlyList<DonationListItem>> ListDonationsAsync(int institutionId, Guid callerId, DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>One of the institution's receipts, or <see langword="null"/>.</summary>
    Task<ReceiptView?> GetInstitutionReceiptAsync(int institutionId, Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>One of the donor's own receipts, in any institution, or <see langword="null"/>.</summary>
    Task<ReceiptView?> GetDonorReceiptAsync(Guid userId, Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>The donor's gifts, newest first.</summary>
    Task<IReadOnlyList<MyDonationItem>> ListMyDonationsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The payment methods staff may choose at the counter, in order.</summary>
    Task<IReadOnlyList<LookupItem>> ListCounterMethodsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default);

    /// <summary>The receipt text's details for <paramref name="publicId"/> (as the platform, from the outbox), or <see langword="null"/>.</summary>
    Task<DonationTextDetails?> GetReceiptTextAsync(Guid publicId, CancellationToken cancellationToken = default);
}

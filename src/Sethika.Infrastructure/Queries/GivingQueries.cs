using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for giving (plan 09): the institution's list, a receipt, My giving, the receipt text.</summary>
/// <remarks>
/// Row-Level Security keeps a donation to its institution and its donor's account
/// (<c>sec.fn_TenantOrOwner</c>); each query names the institution or the account as well, so a platform
/// connection still reads only what the caller may.
/// </remarks>
public sealed class GivingQueries(IDbConnectionFactory connections) : IGivingQueries
{
    private const string ListSql =
        """
        SELECT d.PublicId AS PublicId, d.ReceiptNo AS ReceiptNo, d.Kind AS Kind, d.Amount AS Amount,
               m.Code AS Method, d.ItemDescription AS Items, d.Quantity AS Quantity, d.DonatedAt AS DonatedAt,
               d.DonorName AS DonorName, CAST(CASE WHEN d.InstitutionDonorId IS NULL THEN 0 ELSE 1 END AS BIT) AS FromDonorBook,
               r.FullName AS RecordedByName, CAST(CASE WHEN d.RecordedBy = @callerId THEN 1 ELSE 0 END AS BIT) AS RecordedByMe,
               d.IsVoided AS IsVoided, d.VoidedAt AS VoidedAt, d.RowVersion AS Version
        FROM giving.Donations AS d
        JOIN auth.Users AS r ON r.Id = d.RecordedBy
        LEFT JOIN core.LookupValues AS m ON m.Id = d.PaymentMethodLookupId
        WHERE d.InstitutionId = @institutionId AND d.DonatedAt >= @from AND d.DonatedAt < @to
        ORDER BY d.DonatedAt DESC, d.Id DESC;
        """;

    private const string ReceiptSql =
        """
        SELECT d.PublicId AS PublicId, d.ReceiptNo AS ReceiptNo, d.Kind AS Kind, d.Amount AS Amount,
               d.CurrencyCode AS CurrencyCode, m.Code AS Method, d.ItemDescription AS Items, d.Quantity AS Quantity,
               d.EstimatedValue AS EstimatedValue, d.Note AS Note, d.DonatedAt AS DonatedAt, d.RecordedAt AS RecordedAt,
               d.DonorName AS DonorName, r.FullName AS RecordedByName,
               i.Name AS Name, i.NameSi AS NameSi, i.NameTa AS NameTa, i.Slug AS Slug,
               i.AddressLine1 AS AddressLine1, i.AddressLine2 AS AddressLine2, i.City AS City, i.Phone AS Phone,
               d.IsVoided AS IsVoided, d.VoidReason AS VoidReason, d.VoidedAt AS VoidedAt, v.FullName AS VoidedByName
        FROM giving.Donations AS d
        JOIN org.Institutions AS i ON i.Id = d.InstitutionId
        JOIN auth.Users AS r ON r.Id = d.RecordedBy
        LEFT JOIN auth.Users AS v ON v.Id = d.VoidedBy
        LEFT JOIN core.LookupValues AS m ON m.Id = d.PaymentMethodLookupId
        WHERE d.PublicId = @publicId
        """;

    private const string InstitutionReceiptSql = ReceiptSql + " AND d.InstitutionId = @institutionId;";

    private const string DonorReceiptSql = ReceiptSql + " AND d.DonorUserId = @userId;";

    private const string MineSql =
        """
        SELECT d.PublicId AS PublicId, d.ReceiptNo AS ReceiptNo, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, i.Slug AS InstitutionSlug, d.Kind AS Kind, d.Amount AS Amount,
               d.ItemDescription AS Items, d.Quantity AS Quantity, d.DonatedAt AS DonatedAt, d.IsVoided AS IsVoided
        FROM giving.Donations AS d
        JOIN org.Institutions AS i ON i.Id = d.InstitutionId
        WHERE d.DonorUserId = @userId
        ORDER BY d.DonatedAt DESC, d.Id DESC;
        """;

    private const string MethodsSql =
        """
        SELECT v.Code AS Code, v.Name AS Name, v.NameSi AS NameSi, v.NameTa AS NameTa
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId AND t.Code = 'PAYMENT_METHOD'
        WHERE v.IsActive = 1 AND v.Code IN @codes
        ORDER BY v.SortOrder, v.Code;
        """;

    private const string TextSql =
        """
        SELECT d.InstitutionId AS InstitutionId, d.ReceiptNo AS ReceiptNo, d.Kind AS Kind, d.Amount AS Amount,
               d.IsVoided AS IsVoided, d.DonorUserId AS DonorUserId, b.Phone AS DonorBookPhone,
               COALESCE(u.PreferredLanguage, b.Language) AS Language,
               CAST(CASE WHEN b.TextConsentAt IS NULL THEN 0 ELSE 1 END AS BIT) AS TextConsent,
               i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa
        FROM giving.Donations AS d
        JOIN org.Institutions AS i ON i.Id = d.InstitutionId
        LEFT JOIN org.InstitutionDonors AS b ON b.Id = d.InstitutionDonorId AND b.InstitutionId = d.InstitutionId
        LEFT JOIN auth.Users AS u ON u.Id = d.DonorUserId
        WHERE d.PublicId = @publicId;
        """;

    public async Task<IReadOnlyList<DonationListItem>> ListDonationsAsync(int institutionId, Guid callerId, DateOnly month, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var from = SriLankaTime.ToUtc(first, TimeOnly.MinValue);
        var to = SriLankaTime.ToUtc(first.AddMonths(1), TimeOnly.MinValue);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ListRow>(new CommandDefinition(
            ListSql,
            new { institutionId, callerId, from, to },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new DonationListItem(
            r.PublicId, r.ReceiptNo, r.Kind, r.Amount, r.Method, r.Items, r.Quantity, Utc(r.DonatedAt), r.DonorName,
            r.FromDonorBook, r.RecordedByName, r.RecordedByMe, r.IsVoided, r.VoidedAt is { } v ? Utc(v) : null, r.Version))];
    }

    public Task<ReceiptView?> GetInstitutionReceiptAsync(int institutionId, Guid publicId, CancellationToken cancellationToken = default) =>
        ReceiptAsync(InstitutionReceiptSql, new { publicId, institutionId }, cancellationToken);

    public Task<ReceiptView?> GetDonorReceiptAsync(Guid userId, Guid publicId, CancellationToken cancellationToken = default) =>
        ReceiptAsync(DonorReceiptSql, new { publicId, userId }, cancellationToken);

    public async Task<IReadOnlyList<MyDonationItem>> ListMyDonationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MineRow>(new CommandDefinition(
            MineSql,
            new { userId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new MyDonationItem(
            r.PublicId, r.ReceiptNo, r.InstitutionName, r.InstitutionNameSi, r.InstitutionNameTa, r.InstitutionSlug,
            r.Kind, r.Amount, r.Items, r.Quantity, Utc(r.DonatedAt), r.IsVoided))];
    }

    public async Task<IReadOnlyList<LookupItem>> ListCounterMethodsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(
            MethodsSql,
            new { codes },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<DonationTextDetails?> GetReceiptTextAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<DonationTextDetails>(new CommandDefinition(
            TextSql,
            new { publicId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<ReceiptView?> ReceiptAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<ReceiptRow>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new ReceiptView(
                row.PublicId, row.ReceiptNo, row.Kind, row.Amount, row.CurrencyCode, row.Method, row.Items, row.Quantity,
                row.EstimatedValue, row.Note, Utc(row.DonatedAt), Utc(row.RecordedAt), row.DonorName, row.RecordedByName,
                new ReceiptInstitution(row.Name, row.NameSi, row.NameTa, row.Slug, row.AddressLine1, row.AddressLine2, row.City, row.Phone),
                row.IsVoided, row.VoidReason, row.VoidedAt is { } v ? Utc(v) : null, row.VoidedByName);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class ListRow
    {
        public Guid PublicId { get; init; }
        public string ReceiptNo { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public decimal? Amount { get; init; }
        public string? Method { get; init; }
        public string? Items { get; init; }
        public decimal? Quantity { get; init; }
        public DateTime DonatedAt { get; init; }
        public string DonorName { get; init; } = null!;
        public bool FromDonorBook { get; init; }
        public string RecordedByName { get; init; } = null!;
        public bool RecordedByMe { get; init; }
        public bool IsVoided { get; init; }
        public DateTime? VoidedAt { get; init; }
        public byte[] Version { get; init; } = [];
    }

    private sealed class ReceiptRow
    {
        public Guid PublicId { get; init; }
        public string ReceiptNo { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public decimal? Amount { get; init; }
        public string CurrencyCode { get; init; } = null!;
        public string? Method { get; init; }
        public string? Items { get; init; }
        public decimal? Quantity { get; init; }
        public decimal? EstimatedValue { get; init; }
        public string? Note { get; init; }
        public DateTime DonatedAt { get; init; }
        public DateTime RecordedAt { get; init; }
        public string DonorName { get; init; } = null!;
        public string RecordedByName { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string Slug { get; init; } = null!;
        public string AddressLine1 { get; init; } = null!;
        public string? AddressLine2 { get; init; }
        public string City { get; init; } = null!;
        public string Phone { get; init; } = null!;
        public bool IsVoided { get; init; }
        public string? VoidReason { get; init; }
        public DateTime? VoidedAt { get; init; }
        public string? VoidedByName { get; init; }
    }

    private sealed class MineRow
    {
        public Guid PublicId { get; init; }
        public string ReceiptNo { get; init; } = null!;
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public string InstitutionSlug { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public decimal? Amount { get; init; }
        public string? Items { get; init; }
        public decimal? Quantity { get; init; }
        public DateTime DonatedAt { get; init; }
        public bool IsVoided { get; init; }
    }
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for needs and pledges (plan 09, D.2): the institution's screen, the public board, My giving.</summary>
/// <remarks>
/// <c>giving.Needs</c> has no read filter, so <see cref="BoardSql"/>'s conditions are the whole of what keeps
/// a draft or a closed need off the board.
/// </remarks>
public sealed class NeedQueries(IDbConnectionFactory connections) : INeedQueries
{
    /// <summary>The board shows at most this many: enough to browse, and the filters narrow it.</summary>
    private const int BoardLimit = 200;

    private const string ListSql =
        """
        SELECT n.PublicId AS PublicId, n.Title AS Title, n.Description AS Description, c.Code AS Category, u.Code AS Unit,
               n.QuantityNeeded AS QuantityNeeded, n.QuantityPledged AS QuantityPledged, n.QuantityReceived AS QuantityReceived,
               n.Priority AS Priority, n.Deadline AS Deadline, n.Status AS Status,
               (SELECT COUNT(*) FROM giving.Pledges AS p
                WHERE p.NeedId = n.Id AND p.InstitutionId = n.InstitutionId AND p.Status = 'Pledged') AS OpenPledges,
               n.RowVersion AS Version
        FROM giving.Needs AS n
        JOIN core.LookupValues AS c ON c.Id = n.CategoryLookupId
        JOIN core.LookupValues AS u ON u.Id = n.UnitLookupId
        WHERE n.InstitutionId = @institutionId
        ORDER BY CASE n.Status WHEN 'Open' THEN 0 WHEN 'Draft' THEN 1 ELSE 2 END, n.Id DESC;
        """;

    private const string NeedIsTheirsSql =
        "SELECT COUNT(*) FROM giving.Needs AS n WHERE n.PublicId = @needId AND n.InstitutionId = @institutionId;";

    private const string PledgesSql =
        """
        SELECT p.PublicId AS PublicId, u.FullName AS DonorName, u.PhoneNumber AS DonorPhone, p.Quantity AS Quantity,
               p.ExpectedDeliveryDate AS ExpectedDate, p.Status AS Status, p.ReceivedQuantity AS ReceivedQuantity,
               p.CancelReason AS CancelReason, p.CreatedAt AS CreatedAt, d.PublicId AS ReceiptPublicId,
               d.ReceiptNo AS ReceiptNo, p.RowVersion AS Version
        FROM giving.Pledges AS p
        JOIN giving.Needs AS n ON n.Id = p.NeedId AND n.InstitutionId = p.InstitutionId
        JOIN auth.Users AS u ON u.Id = p.DonorUserId
        LEFT JOIN giving.Donations AS d ON d.PledgeId = p.Id AND d.InstitutionId = p.InstitutionId
        WHERE n.PublicId = @needId AND p.InstitutionId = @institutionId
        ORDER BY CASE p.Status WHEN 'Pledged' THEN 0 ELSE 1 END, p.Id DESC;
        """;

    private const string BoardSql =
        """
        SELECT TOP (@limit)
               n.PublicId AS PublicId, n.Title AS Title, n.Description AS Description, c.Code AS Category, u.Code AS Unit,
               n.QuantityNeeded AS QuantityNeeded, n.QuantityPledged AS QuantityPledged, n.QuantityReceived AS QuantityReceived,
               n.Priority AS Priority, n.Deadline AS Deadline, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, i.Slug AS InstitutionSlug, i.City AS City,
               d.Name AS District, d.NameSi AS DistrictSi, d.NameTa AS DistrictTa
        FROM giving.Needs AS n
        JOIN org.Institutions AS i ON i.Id = n.InstitutionId
        JOIN org.InstitutionTypes AS t ON t.Id = i.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = i.DistrictId
        JOIN core.LookupValues AS c ON c.Id = n.CategoryLookupId
        JOIN core.LookupValues AS u ON u.Id = n.UnitLookupId
        WHERE n.Status = 'Open' AND (n.Deadline IS NULL OR n.Deadline >= @today)
          AND i.Status = 'Active' AND i.IsDeleted = 0
          AND (i.IsPubliclyListed = 1 OR @institution IS NOT NULL)
          AND (@institution IS NULL OR i.Slug = @institution)
          AND (@district IS NULL OR d.Code = @district)
          AND (@type IS NULL OR t.Code = @type)
          AND (@category IS NULL OR c.Code = @category)
        ORDER BY CASE n.Priority WHEN 'Urgent' THEN 0 WHEN 'Normal' THEN 1 ELSE 2 END,
                 CASE WHEN n.Deadline IS NULL THEN 1 ELSE 0 END, n.Deadline, n.Id DESC;
        """;

    private const string MineSql =
        """
        SELECT p.PublicId AS PublicId, n.Title AS NeedTitle, u.Code AS Unit, i.Name AS InstitutionName,
               i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa, i.Slug AS InstitutionSlug, p.Quantity AS Quantity,
               p.ExpectedDeliveryDate AS ExpectedDate, p.Status AS Status, p.ReceivedQuantity AS ReceivedQuantity,
               p.CancelReason AS CancelReason, p.CreatedAt AS CreatedAt, d.PublicId AS ReceiptPublicId, d.ReceiptNo AS ReceiptNo
        FROM giving.Pledges AS p
        JOIN giving.Needs AS n ON n.Id = p.NeedId AND n.InstitutionId = p.InstitutionId
        JOIN org.Institutions AS i ON i.Id = p.InstitutionId
        JOIN core.LookupValues AS u ON u.Id = n.UnitLookupId
        LEFT JOIN giving.Donations AS d ON d.PledgeId = p.Id AND d.InstitutionId = p.InstitutionId
        WHERE p.DonorUserId = @userId
        ORDER BY CASE p.Status WHEN 'Pledged' THEN 0 ELSE 1 END, p.Id DESC;
        """;

    private const string LookupsSql =
        """
        SELECT t.Code AS Type, v.Code AS Code, v.Name AS Name, v.NameSi AS NameSi, v.NameTa AS NameTa
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS t ON t.Id = v.LookupTypeId
        WHERE v.IsActive = 1 AND t.Code IN ('NEED_CATEGORY', 'UNIT')
        ORDER BY v.SortOrder, v.Code;
        """;

    public async Task<IReadOnlyList<NeedListItem>> ListNeedsAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection
            .QueryAsync<NeedListItem>(new CommandDefinition(ListSql, new { institutionId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<IReadOnlyList<NeedPledgeItem>?> ListPledgesAsync(int institutionId, Guid needId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var theirs = await connection
            .ExecuteScalarAsync<int>(new CommandDefinition(NeedIsTheirsSql, new { institutionId, needId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (theirs == 0)
        {
            return null;
        }

        var rows = await connection
            .QueryAsync<NeedPledgeItem>(new CommandDefinition(PledgesSql, new { institutionId, needId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<IReadOnlyList<PublicNeed>> ListBoardAsync(NeedBoardFilter filter, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection
            .QueryAsync<PublicNeed>(new CommandDefinition(
                BoardSql,
                new
                {
                    limit = BoardLimit,
                    today = today.ToDateTime(TimeOnly.MinValue),
                    institution = filter.Institution,
                    district = filter.District,
                    type = filter.Type,
                    category = filter.Category,
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<IReadOnlyList<MyPledgeItem>> ListMyPledgesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection
            .QueryAsync<MyPledgeItem>(new CommandDefinition(MineSql, new { userId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<NeedLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await connection
            .QueryAsync<LookupRow>(new CommandDefinition(LookupsSql, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToList();

        return new NeedLookups(Of(rows, "NEED_CATEGORY"), Of(rows, "UNIT"));
    }

    private static LookupItem[] Of(List<LookupRow> rows, string type) =>
        [.. rows.Where(r => r.Type == type).Select(r => new LookupItem(r.Code, r.Name, r.NameSi, r.NameTa))];

    private sealed record LookupRow(string Type, string Code, string Name, string? NameSi, string? NameTa);
}

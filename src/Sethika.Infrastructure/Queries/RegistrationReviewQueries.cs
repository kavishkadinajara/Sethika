using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>
/// Dapper reads for the reviewer (plan 02, 5.4). The application itself comes from
/// <see cref="IOnboardingApplicationQueries.GetSubmittedAsync"/>, so the applicant and the reviewer
/// read one shape from one query; this adds what only the reviewer needs.
/// </summary>
public sealed class RegistrationReviewQueries(IDbConnectionFactory connections, IOnboardingApplicationQueries applications)
    : IRegistrationReviewQueries
{
    private const string RejectReasonType = "REJECT_REASON";

    private const string ListSql =
        """
        SELECT r.RequestNo AS RequestNo, r.ProposedName AS ProposedName, t.Code AS InstitutionType,
               d.Code AS District, r.City AS City, r.Status AS Status, r.SubmittedAt AS SubmittedAt,
               r.UpdatedAt AS UpdatedAt
        FROM org.RegistrationRequests AS r
        JOIN org.InstitutionTypes AS t ON t.Id = r.InstitutionTypeId
        JOIN core.Districts AS d ON d.Id = r.DistrictId
        WHERE r.Status IN @statuses
        ORDER BY r.SubmittedAt, r.Id
        OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

        SELECT COUNT(*) FROM org.RegistrationRequests AS r WHERE r.Status IN @statuses;
        """;

    // What the application's type requires, its checklist with any answers, and the reasons a
    // rejection can give. Drafts are left out here too, so a draft's number reads as nothing at all.
    private const string ExtrasSql =
        """
        SELECT dt.Code AS Code, dt.Name AS Name, req.IsMandatory AS IsMandatory
        FROM org.RegistrationRequests AS r
        JOIN org.InstitutionTypeRequiredDocuments AS req ON req.InstitutionTypeId = r.InstitutionTypeId
        JOIN org.DocumentTypes AS dt ON dt.Id = req.DocumentTypeId AND dt.IsActive = 1
        WHERE r.RequestNo = @requestNo AND r.Status <> 'Draft'
        ORDER BY req.SortOrder, dt.Code;

        SELECT i.Code AS Code, i.Question AS Question, i.IsMandatory AS IsMandatory,
               e.IsPassed AS IsPassed, e.Remarks AS Remarks, e.CheckedAt AS CheckedAt
        FROM org.RegistrationRequests AS r
        JOIN org.VerificationChecklistItems AS i
          ON i.IsActive = 1 AND (i.InstitutionTypeId IS NULL OR i.InstitutionTypeId = r.InstitutionTypeId)
        LEFT JOIN org.RegistrationRequestChecklist AS e ON e.RequestId = r.Id AND e.ChecklistItemId = i.Id
        WHERE r.RequestNo = @requestNo AND r.Status <> 'Draft'
        ORDER BY i.SortOrder, i.Code;

        SELECT v.Code AS Code, v.Name AS Name
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS lt ON lt.Id = v.LookupTypeId AND lt.Code = @rejectReasonType
        WHERE v.IsActive = 1
        ORDER BY v.SortOrder, v.Code;
        """;

    private const string ResolveRejectReasonSql =
        """
        SELECT v.Id
        FROM core.LookupValues AS v
        JOIN core.LookupTypes AS lt ON lt.Id = v.LookupTypeId AND lt.Code = @rejectReasonType
        WHERE v.Code = @code AND v.IsActive = 1;
        """;

    /// <summary>The work still to do, when no status is asked for.</summary>
    private static readonly string[] Open = ["Submitted", "UnderReview", "InfoRequested"];

    public async Task<ReviewQueuePage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            ListSql,
            new { statuses = status is null ? Open : [status], skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var items = (await grid.ReadAsync<ReviewQueueItem>().ConfigureAwait(false)).AsList();
        var total = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return new ReviewQueuePage(items, total, page, pageSize);
    }

    public async Task<ReviewView?> GetAsync(string requestNo, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetSubmittedAsync(requestNo, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            ExtrasSql,
            new { requestNo, rejectReasonType = RejectReasonType },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new ReviewView(
            application,
            [.. await grid.ReadAsync<ReviewRequiredDocument>().ConfigureAwait(false)],
            [.. await grid.ReadAsync<ReviewChecklistItem>().ConfigureAwait(false)],
            [.. await grid.ReadAsync<RejectReasonOption>().ConfigureAwait(false)]);
    }

    public async Task<int?> ResolveRejectReasonAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            ResolveRejectReasonSql,
            new { code, rejectReasonType = RejectReasonType },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper read of the current legal documents, per the reads-use-Dapper rule.</summary>
public sealed class LegalDocumentQueries(IDbConnectionFactory connections) : ILegalDocumentQueries
{
    // A document with no text in the requested language falls back to English, rather than
    // disappearing from the signup screen and leaving the donor unable to consent at all.
    private const string CurrentSql =
        """
        SELECT d.Code          AS Code,
               d.Version       AS Version,
               d.Language      AS Language,
               d.Title         AS Title,
               d.Content       AS Content,
               d.ContentSha256 AS ContentSha256
        FROM core.LegalDocuments AS d
        WHERE d.IsCurrent = 1
          AND (d.Language = @language
               OR (d.Language = 'en'
                   AND NOT EXISTS (SELECT 1 FROM core.LegalDocuments AS l
                                   WHERE l.Code = d.Code AND l.IsCurrent = 1 AND l.Language = @language)))
        ORDER BY d.Code;
        """;

    public async Task<IReadOnlyList<CurrentLegalDocument>> GetCurrentAsync(
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<CurrentLegalDocument>(new CommandDefinition(
            CurrentSql,
            new { language },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }
}

using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Asks Expo what became of the pushes it took (plan F, F29). An <c>ok</c> ticket only means Expo has
/// the push; the receipt says what FCM or APNs did with it, and is the usual way to learn that a
/// phone has gone.
/// </summary>
/// <remarks>
/// A row is asked about from 15 minutes after the send, as Expo advises, until its receipt is cleared
/// after 24 hours. A receipt that never came leaves the row <c>Sent</c>, which is all anyone knows.
/// </remarks>
public sealed partial class PushReceipts(
    IDbConnectionFactory connections,
    IPushGateway gateway,
    IOptions<PushOptions> options,
    ILogger<PushReceipts> logger)
{
    private const string PendingSql =
        """
        SELECT TOP (@batchSize)
               n.Id                AS Id,
               n.ProviderMessageId AS TicketId,
               n.Recipient         AS Token
        FROM notify.Notifications AS n
        WHERE n.Channel = 'Push'
          AND n.Status = 'Sent'
          AND n.SentAt <= DATEADD(MINUTE, -15, SYSUTCDATETIME())
          AND n.SentAt > DATEADD(HOUR, -24, SYSUTCDATETIME())
          AND n.ProviderMessageId IS NOT NULL
          AND n.Id > @after
        ORDER BY n.Id;
        """;

    private const string DeliveredSql =
        "UPDATE notify.Notifications SET Status = 'Delivered' WHERE Id IN @ids;";

    private const string FailedSql =
        "UPDATE notify.Notifications SET Status = 'Failed', ErrorMessage = LEFT(@error, 500) WHERE Id = @id;";

    /// <summary>One pass over every row that may have a receipt.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = options.Value.ReceiptBatchSize;
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Paged by id, not by what is still Sent: a row whose receipt is not ready stays Sent, and
        // would otherwise come back as the first page again.
        long after = 0;
        int delivered = 0, failed = 0;
        while (true)
        {
            var rows = (await connection.QueryAsync<Pending>(new CommandDefinition(
                PendingSql,
                new { batchSize, after },
                cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

            if (rows.Count == 0)
            {
                break;
            }

            var receipts = await gateway.GetReceiptsAsync(rows.ConvertAll(r => r.TicketId), cancellationToken).ConfigureAwait(false);

            var good = new List<long>();
            foreach (var row in rows)
            {
                if (!receipts.TryGetValue(row.TicketId, out var receipt))
                {
                    continue;
                }

                if (receipt.Error is null)
                {
                    good.Add(row.Id);
                    continue;
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    FailedSql,
                    new { id = row.Id, error = receipt.Error },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                failed++;

                if (receipt.DeviceIsGone)
                {
                    await PushDevices.DisableGoneAsync(connection, row.Token, cancellationToken).ConfigureAwait(false);
                }
            }

            if (good.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    DeliveredSql,
                    new { ids = good },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                delivered += good.Count;
            }

            if (rows.Count < batchSize)
            {
                break;
            }

            after = rows[^1].Id;
        }

        LogChecked(logger, delivered, failed);
    }

    private sealed record Pending(long Id, string TicketId, string Token);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push receipts: {Delivered} delivered, {Failed} failed.")]
    private static partial void LogChecked(ILogger logger, int delivered, int failed);
}

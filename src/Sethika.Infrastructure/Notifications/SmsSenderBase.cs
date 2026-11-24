using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// The part of sending an SMS that does not depend on the gateway: recording it in
/// <c>notify.Notifications</c>, honouring the dedup key, and costing the message.
/// </summary>
/// <remarks>
/// Both senders inherit this, so the console sender exercises the same delivery log, dedup index
/// and cost columns as the real one. That is what makes switching providers a change of one class
/// rather than a change of data path - and it means the SMS cost tracking is being tested long
/// before a gateway exists to test it against.
/// </remarks>
public abstract class SmsSenderBase(IDbConnectionFactory connectionFactory, SmsOptions options)
    : ISmsSender
{
    /// <summary>The configured options, for use by derived senders.</summary>
    protected SmsOptions Options { get; } = options;

    /// <summary>
    /// Claims the row for this message, then hands the message to the gateway.
    /// </summary>
    /// <remarks>
    /// The row is written <em>before</em> the send and updated after. Sending first and recording
    /// afterwards would lose the record of any message whose process died mid-send, which is the one
    /// case the log exists for: a user insisting no code arrived, and no way to tell whether it was
    /// ever sent.
    /// </remarks>
    public async Task SendAsync(
        string phoneNumber,
        string message,
        string? dedupKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var notificationId = await ClaimAsync(connection, phoneNumber, message, dedupKey, cancellationToken)
            .ConfigureAwait(false);

        if (notificationId is null)
        {
            // Another send already holds this dedup key and did not fail. Returning quietly is the
            // point of the key: a retried Hangfire job must not text the same person a second time.
            return;
        }

        try
        {
            var providerMessageId = await DeliverAsync(phoneNumber, message, cancellationToken)
                .ConfigureAwait(false);

            await MarkSentAsync(connection, notificationId.Value, providerMessageId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Record the failure, then rethrow. Swallowing it would leave the caller believing a
            // code was delivered; losing the reason would leave nothing to diagnose.
            await MarkFailedAsync(connection, notificationId.Value, ex.Message, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Hands the message to the gateway and returns its message id, if it gives one.</summary>
    protected abstract Task<string?> DeliverAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the queued row, or takes back a failed one holding <paramref name="dedupKey"/>, or returns
    /// null when the key is held by a send that did not fail.
    /// </summary>
    /// <remarks>
    /// Uniqueness is enforced solely by <c>UX_Notifications_Dedup</c> rejecting the second insert.
    /// There is no <c>NOT EXISTS</c> pre-check, because it would not add anything: a check and an
    /// insert are two statements, so two workers can both see nothing and both proceed, and the
    /// index has to catch it regardless. A pre-check would only make the SQL look safer than the
    /// mechanism actually is - mutation testing confirmed it, since removing the clause changed no
    /// test outcome while removing the catch below broke two.
    /// </remarks>
    private async Task<long?> ClaimAsync(
        DbConnection connection,
        string phoneNumber,
        string message,
        string? dedupKey,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO notify.Notifications
                (Channel, Recipient, Body, DedupKey, Status, ScheduledAt, Attempts, SmsSegments, CostLkr)
            VALUES
                ('SMS', @recipient, @body, @dedupKey, 'Queued', SYSUTCDATETIME(), 1, @segments, @cost);

            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;

        var segments = CountSegments(message);

        var command = new CommandDefinition(
            sql,
            new
            {
                recipient = phoneNumber,
                body = message,
                dedupKey,
                segments,
                cost = segments * Options.CostPerSegmentLkr,
            },
            cancellationToken: cancellationToken);

        try
        {
            return await connection.QuerySingleOrDefaultAsync<long?>(command).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // 2601/2627 are duplicate-key violations: another send already holds this dedup key. This
            // catch is the entire dedup guarantee - removing it makes two tests fail, including the
            // concurrent one. The key is only taken back if the earlier send failed.
            return await ReclaimFailedAsync(connection, phoneNumber, message, dedupKey!, segments, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes back the row holding <paramref name="dedupKey"/> when its send failed, or returns null.
    /// </summary>
    /// <remarks>
    /// Without this a failed text was never sent: the retry's insert was refused by the key, it
    /// returned quietly, and the caller marked its work done (plan F, 10.6). Only a <c>Failed</c> row
    /// is taken: a <c>Sent</c> one must stay sent, and a <c>Queued</c> one is being sent right now.
    /// The status test and the change are one statement, so of two retries racing for the same failed
    /// row, one gets it and the other finds it <c>Queued</c> and stops.
    /// </remarks>
    private async Task<long?> ReclaimFailedAsync(
        DbConnection connection,
        string phoneNumber,
        string message,
        string dedupKey,
        int segments,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE notify.Notifications
            SET Status = 'Queued',
                Recipient = @recipient,
                Body = @body,
                Attempts = Attempts + 1,
                SmsSegments = @segments,
                CostLkr = @cost,
                ErrorMessage = NULL
            OUTPUT inserted.Id
            WHERE DedupKey = @dedupKey AND Status = 'Failed';
            """;

        return await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            sql,
            new
            {
                recipient = phoneNumber,
                body = message,
                dedupKey,
                segments,
                cost = segments * Options.CostPerSegmentLkr,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task MarkSentAsync(
        DbConnection connection,
        long notificationId,
        string? providerMessageId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE notify.Notifications
            SET Status = 'Sent',
                SentAt = SYSUTCDATETIME(),
                ProviderMessageId = @providerMessageId
            WHERE Id = @notificationId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { notificationId, providerMessageId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task MarkFailedAsync(
        DbConnection connection,
        long notificationId,
        string error,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE notify.Notifications
            SET Status = 'Failed',
                ErrorMessage = LEFT(@error, 500)
            WHERE Id = @notificationId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { notificationId, error },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Billable segments for <paramref name="message"/>.
    /// </summary>
    /// <remarks>
    /// Any character outside GSM-7 forces the whole message to UCS-2, which cuts the segment from
    /// 160 characters to 70 - so one Sinhala character in an otherwise English message more than
    /// doubles its cost. The check is deliberately coarse (non-ASCII implies UCS-2); the real GSM-7
    /// set includes a handful of non-ASCII characters and excludes a few ASCII ones, which would
    /// make this exact but matters only once a gateway bill exists to reconcile against.
    /// </remarks>
    private int CountSegments(string message)
    {
        if (message.Length == 0)
        {
            return 1;
        }

        var isUnicode = message.Any(c => c > 127);
        var segmentLength = isUnicode ? Options.UnicodeSegmentLength : Options.SegmentLength;

        return (int)Math.Ceiling(message.Length / (double)segmentLength);
    }

    /// <summary>Formats a value for a log line without depending on the ambient culture.</summary>
    protected static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

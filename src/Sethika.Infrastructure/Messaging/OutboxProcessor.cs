using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Messaging;

/// <summary>What one batch, or one run, of the processor did.</summary>
/// <param name="Claimed">Messages taken from the queue.</param>
/// <param name="Handled">Handled and marked processed.</param>
/// <param name="Unhandled">No handler takes the event type; marked processed without one.</param>
/// <param name="Failed">The handler threw; the attempt was counted and the message left for a later run.</param>
public sealed record OutboxRun(int Claimed, int Handled, int Unhandled, int Failed)
{
    public static OutboxRun None { get; } = new(0, 0, 0, 0);

    public OutboxRun Add(OutboxRun other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Claimed + other.Claimed, Handled + other.Handled, Unhandled + other.Unhandled, Failed + other.Failed);
    }
}

/// <summary>
/// Sends what <c>core.OutboxMessages</c> holds (plan 02, B10): claims a batch, dispatches each message
/// to the handler for its event type, and marks it processed or counts the failure.
/// </summary>
/// <remarks>
/// <para>
/// A batch is claimed with <c>UPDLOCK, READPAST</c> inside a transaction held until the batch is
/// done. The update lock keeps a second worker from taking the same rows; READPAST lets it skip them
/// and take the next ones instead of queueing behind the first. Two runs can overlap - a run longer
/// than the minute between them, or two API instances - and each message is still handled by one.
/// </para>
/// <para>
/// A crash mid-batch rolls the whole batch back, including messages whose side effect already
/// happened; the next run hands those over again, which is why every handler is idempotent
/// (<see cref="IOutboxHandler"/>).
/// </para>
/// <para>
/// Runs on a connection the caller's scope stamps. The job gives it the platform identity
/// (<c>JobScopes</c>), so a handler reading tenant tables sees every tenant's rows.
/// </para>
/// </remarks>
public sealed partial class OutboxProcessor
{
    private const string ClaimSql =
        """
        SELECT TOP (@batchSize)
               m.Id            AS Id,
               m.EventType     AS EventType,
               m.AggregateType AS AggregateType,
               m.AggregateId   AS AggregateId,
               m.Payload       AS Payload
        FROM core.OutboxMessages AS m WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE m.ProcessedAt IS NULL AND m.Attempts < @maxAttempts
        ORDER BY m.OccurredAt, m.Id;
        """;

    // LastError stays: a message that failed and then went through keeps the record of why it was late.
    private const string ProcessedSql =
        "UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE Id = @id;";

    private const string FailedSql =
        "UPDATE core.OutboxMessages SET Attempts = Attempts + 1, LastError = LEFT(@error, 1000) WHERE Id = @id;";

    private readonly IDbConnectionFactory _connections;
    private readonly Dictionary<string, IOutboxHandler> _handlers = new(StringComparer.Ordinal);
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IDbConnectionFactory connections,
        IEnumerable<IOutboxHandler> handlers,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(options);

        _connections = connections;
        _options = options.Value;
        _logger = logger;

        foreach (var handler in handlers)
        {
            foreach (var eventType in handler.EventTypes)
            {
                if (!_handlers.TryAdd(eventType, handler))
                {
                    throw new InvalidOperationException(
                        $"Two outbox handlers take '{eventType}': {_handlers[eventType].GetType().Name} and {handler.GetType().Name}.");
                }
            }
        }
    }

    /// <summary>
    /// Works through full batches until one comes back short or with a failure, up to
    /// <see cref="OutboxOptions.MaxBatchesPerRun"/>.
    /// </summary>
    /// <remarks>
    /// Stopping at a failure is what makes <see cref="OutboxOptions.MaxAttempts"/> mean runs, not
    /// moments: the failed message would be the first one the next batch claims, and five attempts
    /// would be spent in a second.
    /// </remarks>
    public async Task<OutboxRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var total = OutboxRun.None;
        for (var round = 0; round < _options.MaxBatchesPerRun; round++)
        {
            var batch = await ProcessBatchAsync(cancellationToken).ConfigureAwait(false);
            total = total.Add(batch);

            if (batch.Claimed < _options.BatchSize || batch.Failed > 0)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>Claims one batch, handles it, and commits.</summary>
    public async Task<OutboxRun> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var claimed = (await connection.QueryAsync<OutboxEvent>(new CommandDefinition(
            ClaimSql,
            new { batchSize = _options.BatchSize, maxAttempts = _options.MaxAttempts },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        int handled = 0, unhandled = 0, failed = 0;
        foreach (var message in claimed)
        {
            if (!_handlers.TryGetValue(message.EventType, out var handler))
            {
                // Signup, password and role events are in the table already and have nothing to send.
                // Left unprocessed they would be claimed again by every run and crowd out the rest.
                LogUnhandled(_logger, message.Id, message.EventType);
                await MarkProcessedAsync(connection, transaction, message.Id, cancellationToken).ConfigureAwait(false);
                unhandled++;
                continue;
            }

            try
            {
                await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // One message's failure is that message's: it is counted and the batch goes on.
                LogFailed(_logger, ex, message.Id, message.EventType);
                await connection.ExecuteAsync(new CommandDefinition(
                    FailedSql,
                    new { id = message.Id, error = ex.Message },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                failed++;
                continue;
            }

            await MarkProcessedAsync(connection, transaction, message.Id, cancellationToken).ConfigureAwait(false);
            handled++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new OutboxRun(claimed.Count, handled, unhandled, failed);
    }

    private static Task<int> MarkProcessedAsync(DbConnection connection, DbTransaction transaction, long id, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(ProcessedSql, new { id }, transaction, cancellationToken: cancellationToken));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Outbox message {MessageId} ({EventType}) has no handler; marked processed.")]
    private static partial void LogUnhandled(ILogger logger, long messageId, string eventType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) failed; it will be retried.")]
    private static partial void LogFailed(ILogger logger, Exception exception, long messageId, string eventType);
}

using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Messaging;

/// <summary>Plan 02, 5.7: <see cref="OutboxProcessor"/> against <c>core.OutboxMessages</c> in SQL Server.</summary>
/// <remarks>
/// The processor takes every pending row, so each test first marks what earlier tests left pending
/// as processed (nothing reads <c>ProcessedAt</c> but the processor) and then works with its own
/// rows, under event types no real handler takes.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class OutboxProcessorTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string AggregateType = "OutboxTest";

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;");
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM core.OutboxMessages WHERE AggregateType = @AggregateType;", new { AggregateType });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Records what it was given; throws for <c>Test.Fails</c>; holds <see cref="Gate"/> for one id.</summary>
    private sealed class TestHandler : IOutboxHandler
    {
        public ConcurrentQueue<long> Seen { get; } = new();

        public long? HoldAt { get; init; }

        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> EventTypes { get; init; } = ["Test.Recorded", "Test.Fails"];

        public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
        {
            Seen.Enqueue(message.Id);
            if (message.Id == HoldAt)
            {
                Holding.TrySetResult();
                await Gate.Task.WaitAsync(cancellationToken);
            }

            if (message.EventType == "Test.Fails")
            {
                throw new InvalidOperationException("boom");
            }
        }
    }

    private OutboxProcessor Processor(IOutboxHandler handler, int batchSize = 50, int maxBatches = 20) => new(
        new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform()),
        [handler],
        Options.Create(new OutboxOptions { BatchSize = batchSize, MaxAttempts = 5, MaxBatchesPerRun = maxBatches }),
        NullLogger<OutboxProcessor>.Instance);

    /// <summary>Inserts messages one millisecond apart, so their order is the order given.</summary>
    private async Task<long[]> InsertAsync(params string[] eventTypes)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var ids = new List<long>();
        var at = DateTime.UtcNow.AddMinutes(-1);
        foreach (var eventType in eventTypes)
        {
            at = at.AddMilliseconds(1);
            ids.Add(await connection.ExecuteScalarAsync<long>(
                """
                INSERT core.OutboxMessages (EventType, AggregateType, AggregateId, Payload, OccurredAt)
                OUTPUT inserted.Id
                VALUES (@eventType, @AggregateType, 'x', N'{}', @at);
                """,
                new { eventType, AggregateType, at }));
        }

        return [.. ids];
    }

    private sealed record Row(long Id, bool Processed, byte Attempts, string? LastError);

    private async Task<Dictionary<long, Row>> RowsAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var rows = await connection.QueryAsync<Row>(
            """
            SELECT Id AS Id, CAST(IIF(ProcessedAt IS NULL, 0, 1) AS BIT) AS Processed, Attempts AS Attempts, LastError AS LastError
            FROM core.OutboxMessages WHERE AggregateType = @AggregateType;
            """,
            new { AggregateType });
        return rows.ToDictionary(r => r.Id);
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task HandledMessagesAreMarkedProcessed_OldestFirst_AndOnesNobodyHandlesToo()
    {
        var ids = await InsertAsync("Test.Recorded", "Test.Nobody", "Test.Recorded");

        // Inserted last, happened first: the order is when it happened, not the row's id.
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE core.OutboxMessages SET OccurredAt = DATEADD(MINUTE, -5, OccurredAt) WHERE Id = @id;", new { id = ids[2] });
        }

        var handler = new TestHandler();

        var run = await Processor(handler).RunAsync();

        Assert.Equal(new OutboxRun(3, 2, 1, 0), run);
        Assert.Equal([ids[2], ids[0]], handler.Seen);
        Assert.All((await RowsAsync()).Values, r => Assert.Equal((true, (byte)0, (string?)null), (r.Processed, r.Attempts, r.LastError)));
    }

    [Fact]
    public async Task AFailureIsCountedAndLeftForALaterRun_UntilTheFifth()
    {
        var ids = await InsertAsync("Test.Fails", "Test.Recorded");
        var processor = Processor(new TestHandler());

        // The rest of the batch goes on without it.
        Assert.Equal(new OutboxRun(2, 1, 0, 1), await processor.RunAsync());
        var rows = await RowsAsync();
        Assert.Equal((false, (byte)1, "boom"), (rows[ids[0]].Processed, rows[ids[0]].Attempts, rows[ids[0]].LastError));
        Assert.True(rows[ids[1]].Processed);

        for (var run = 2; run <= 5; run++)
        {
            Assert.Equal(1, (await processor.RunAsync()).Failed);
        }

        // Five failures: no longer claimed, and the error stays for an operator.
        Assert.Equal(OutboxRun.None, await processor.RunAsync());
        var last = (await RowsAsync())[ids[0]];
        Assert.Equal((false, (byte)5, "boom"), (last.Processed, last.Attempts, last.LastError));
    }

    [Fact]
    public async Task ARunWorksThroughFullBatches_ButStopsAtAFailure()
    {
        // Batches of two: five messages are three batches in one run.
        await InsertAsync("Test.Recorded", "Test.Recorded", "Test.Recorded", "Test.Recorded", "Test.Recorded");
        Assert.Equal(new OutboxRun(5, 5, 0, 0), await Processor(new TestHandler(), batchSize: 2).RunAsync());

        // A failure ends the run, so one run costs a failing message one attempt, not one per batch.
        var ids = await InsertAsync("Test.Fails", "Test.Recorded", "Test.Recorded");
        Assert.Equal(new OutboxRun(1, 0, 0, 1), await Processor(new TestHandler(), batchSize: 1).RunAsync());

        var rows = await RowsAsync();
        Assert.Equal(1, rows[ids[0]].Attempts);
        Assert.False(rows[ids[1]].Processed);
    }

    [Fact]
    public async Task TwoWorkersNeverTakeTheSameMessage_AndTheSecondDoesNotWaitForTheFirst()
    {
        var ids = await InsertAsync("Test.Recorded", "Test.Recorded", "Test.Recorded", "Test.Recorded");
        var handler = new TestHandler { HoldAt = ids[0] };

        // The first worker claims the two oldest and stops inside the first, holding its batch.
        var first = Task.Run(() => Processor(handler, batchSize: 2).ProcessBatchAsync());
        try
        {
            await handler.Holding.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // The second skips the locked rows and takes the next two - while the first still holds.
            var second = await Processor(handler, batchSize: 2).ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(new OutboxRun(2, 2, 0, 0), second);
            Assert.Equal([ids[0], ids[2], ids[3]], handler.Seen);
        }
        finally
        {
            handler.Gate.TrySetResult();
        }

        Assert.Equal(new OutboxRun(2, 2, 0, 0), await first);
        Assert.Equal(ids.Order(), handler.Seen.Order());
        Assert.All((await RowsAsync()).Values, r => Assert.True(r.Processed));
    }

    [Fact]
    public void TwoHandlersForOneEventTypeStopTheProcessor()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new OutboxProcessor(
            new SqlConnectionFactory(database.ConnectionString, TestCurrentUserContext.Platform()),
            [new TestHandler(), new TestHandler { EventTypes = ["Test.Other", "Test.Recorded"] }],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance));

        Assert.Contains("'Test.Recorded'", error.Message, StringComparison.Ordinal);
    }
}

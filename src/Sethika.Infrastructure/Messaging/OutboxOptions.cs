namespace Sethika.Infrastructure.Messaging;

/// <summary>How the outbox processor works through <c>core.OutboxMessages</c>. Section <c>Outbox</c>.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Messages claimed, and locked, per transaction.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Failures before a message is left alone. With one attempt per run and a run a minute, five is
    /// five minutes of retrying; after that the row keeps its <c>LastError</c> for an operator.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Full batches one run works through before leaving the rest to the next.</summary>
    public int MaxBatchesPerRun { get; set; } = 20;
}

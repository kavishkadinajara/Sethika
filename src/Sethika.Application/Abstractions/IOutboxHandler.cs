namespace Sethika.Application.Abstractions;

/// <summary>One <c>core.OutboxMessages</c> row, as a handler sees it.</summary>
public sealed record OutboxEvent(long Id, string EventType, string AggregateType, string AggregateId, string Payload);

/// <summary>Carries out the side effect of one or more outbox event types (plan 02, B10).</summary>
/// <remarks>
/// The processor marks a message processed only after its handler returns, and a crash in between
/// hands the message over again. So every handler must be safe to run twice for one message: an SMS
/// is, through its dedup key. A handler that throws has the message retried on a later run, up to
/// the processor's attempt limit.
/// </remarks>
public interface IOutboxHandler
{
    /// <summary>The event types this handler takes. Two handlers taking one type stop the processor.</summary>
    IReadOnlyCollection<string> EventTypes { get; }

    Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken);
}

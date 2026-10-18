namespace Sethika.Domain.Core;

/// <summary>
/// A side effect waiting to happen - an SMS, an email, a cache invalidation. Maps
/// <c>core.OutboxMessages</c>.
/// </summary>
/// <remarks>
/// Written in the same transaction as the change it describes, then processed by a background job,
/// so a crash after commit cannot lose the side effect and a rollback cannot send one for a change
/// that never happened.
/// </remarks>
public class OutboxMessage
{
    public long Id { get; set; }
    public string EventType { get; set; } = null!;
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;

    /// <summary>JSON. <c>CK_Outbox_Payload</c> rejects anything else.</summary>
    public string Payload { get; set; } = null!;

    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public byte Attempts { get; set; }
    public string? LastError { get; set; }
}

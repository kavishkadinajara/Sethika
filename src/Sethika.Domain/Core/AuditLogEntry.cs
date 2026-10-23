namespace Sethika.Domain.Core;

/// <summary>Who did what to which record, and from where. Maps <c>audit.AuditLogs</c>.</summary>
/// <remarks>
/// Written in the same transaction as the change, so the log and the data never disagree. Values
/// are JSON (<c>CK_AuditLogs_OldValues</c>, <c>CK_AuditLogs_NewValues</c>).
/// </remarks>
public class AuditLogEntry
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public int? ImpersonationSessionId { get; set; }
    public int? InstitutionId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityName { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
}

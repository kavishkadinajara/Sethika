namespace Sethika.Application.Abstractions;

/// <summary>A religious service as the institution's services screen shows and edits it.</summary>
/// <param name="Version">The <c>RowVersion</c> an edit sends back, so it cannot overwrite a newer one.</param>
public sealed record ServiceTypeView(
    Guid PublicId,
    string Name,
    string? NameSi,
    string? NameTa,
    string? Description,
    short DurationMinutes,
    short BufferMinutes,
    byte LeadersRequired,
    string LocationMode,
    short MinAdvanceDays,
    short MaxAdvanceDays,
    bool IsActive,
    byte[] Version);

/// <summary>One span of a leader's usual week.</summary>
/// <param name="DayOfWeek">0 Sunday ... 6 Saturday.</param>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time.</param>
public sealed record WeeklyHoursView(byte DayOfWeek, string StartTime, string EndTime);

/// <summary>A leader as the roster screen shows and edits them.</summary>
/// <param name="HasAccount">Whether the leader signs in, to answer from their own schedule.</param>
/// <param name="Version">The <c>RowVersion</c> an edit sends back.</param>
public sealed record LeaderView(
    Guid PublicId,
    string DisplayName,
    string? NameSi,
    string? NameTa,
    string? Title,
    string? Phone,
    string Language,
    bool HasAccount,
    bool IsActive,
    byte[] Version,
    IReadOnlyList<WeeklyHoursView> WeeklyHours);

/// <summary>Leave not yet over.</summary>
/// <param name="Id">Tenant-internal, never shown to donors (as a blocked date's, plan 03 C9).</param>
/// <param name="StartsAt">UTC.</param>
/// <param name="EndsAt">UTC.</param>
public sealed record TimeOffView(int Id, Guid Leader, string LeaderName, DateTime StartsAt, DateTime EndsAt, string? Reason);

/// <summary>A leader as the assign sheet offers them for one appointment.</summary>
/// <param name="Assigned">Already a live leader of this appointment.</param>
/// <param name="Busy">
/// Why the leader is not free for it: <c>TimeOff</c> or <c>Appointment</c> (another one). <see langword="null"/> when free.
/// </param>
/// <param name="NormallyAvailable">
/// Whether the service falls inside the leader's usual week; <see langword="null"/> when no week is recorded.
/// Only advice: it never stops an assignment (V2).
/// </param>
public sealed record LeaderCandidateView(
    Guid PublicId,
    string DisplayName,
    string? NameSi,
    string? NameTa,
    string? Title,
    bool Assigned,
    string? Busy,
    bool? NormallyAvailable);

/// <summary>Reads for the institution's services, roster and assign screens (Phase E, 8.4).</summary>
public interface IServiceSetupQueries
{
    /// <summary>The institution's service types, active first.</summary>
    Task<IReadOnlyList<ServiceTypeView>> ListTypesAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The institution's leaders, active first, each with their usual week.</summary>
    Task<IReadOnlyList<LeaderView>> ListLeadersAsync(int institutionId, CancellationToken cancellationToken = default);

    /// <summary>The institution's leave that ends after <paramref name="utcNow"/>, soonest first.</summary>
    Task<IReadOnlyList<TimeOffView>> ListTimeOffAsync(int institutionId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active leaders of the institution, for assigning to <paramref name="appointmentNo"/>: the
    /// assigned, then the free and normally available, then the rest. <see langword="null"/> when the
    /// institution has no such appointment.
    /// </summary>
    Task<IReadOnlyList<LeaderCandidateView>?> LeaderCandidatesAsync(int institutionId, string appointmentNo, CancellationToken cancellationToken = default);
}

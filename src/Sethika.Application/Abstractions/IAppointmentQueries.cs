namespace Sethika.Application.Abstractions;

/// <summary>Which appointments the institution's agenda shows (V10).</summary>
public enum AppointmentAgenda
{
    /// <summary>Requested or approved, not yet over: the default, soonest first.</summary>
    Upcoming,

    /// <summary>Requests waiting for a decision, not yet over.</summary>
    Waiting,

    /// <summary>Not yet over and short of leaders: requests, and approved ones after a decline (V6).</summary>
    NeedsLeaders,

    /// <summary>Over, rejected, cancelled or completed: latest first.</summary>
    History,
}

/// <summary>A leader on an appointment, as the agenda shows them. Leaders removed are left out.</summary>
/// <param name="Response"><c>Pending</c>, <c>Accepted</c> or <c>Declined</c>.</param>
public sealed record AppointmentLeaderView(
    Guid PublicId,
    string DisplayName,
    string? NameSi,
    string? NameTa,
    string? Title,
    string Response,
    bool RespondedOnBehalf,
    string? DeclineReason);

/// <summary>One appointment on the institution's agenda.</summary>
/// <param name="StartsAt">UTC.</param>
/// <param name="EndsAt">UTC.</param>
/// <param name="DonorPhone">Masked in the database unless the caller may see it (D9).</param>
/// <param name="NeedsLeaders">Fewer live leaders than the service needs, while it can still happen (V6).</param>
public sealed record AppointmentAgendaView(
    string AppointmentNo,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid ServiceType,
    string ServiceName,
    string? ServiceNameSi,
    string? ServiceNameTa,
    byte LeadersRequired,
    string LocationMode,
    string? Address,
    string? Occasion,
    string? DonorNote,
    string DonorName,
    string DonorPhone,
    DateTime CreatedAt,
    string? RejectReason,
    string? CancelReason,
    bool NeedsLeaders,
    IReadOnlyList<AppointmentLeaderView> Leaders);

/// <summary>One appointment on a signed-in leader's own schedule.</summary>
/// <param name="Response">Their answer: <c>Pending</c> or <c>Accepted</c>.</param>
public sealed record LeaderScheduleView(
    string AppointmentNo,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid ServiceType,
    string ServiceName,
    string? ServiceNameSi,
    string? ServiceNameTa,
    string LocationMode,
    string? Address,
    string? Occasion,
    string DonorName,
    string DonorPhone,
    string Response);

/// <summary>Dapper reads for the institution's appointments screen and a leader's schedule (Phase E, 8.7).</summary>
public interface IAppointmentQueries
{
    /// <summary>The institution's appointments in <paramref name="agenda"/>, at most <see cref="MaxAgenda"/>.</summary>
    Task<IReadOnlyList<AppointmentAgendaView>> AgendaAsync(int institutionId, AppointmentAgenda agenda, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the leader linked to <paramref name="userId"/> on the institution's roster still has to do:
    /// approved appointments not yet over where their answer is pending or accepted, soonest first.
    /// Empty when the account is no leader there.
    /// </summary>
    Task<IReadOnlyList<LeaderScheduleView>> ScheduleAsync(int institutionId, Guid userId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>The most appointments <see cref="AgendaAsync"/> returns; the list has no paging yet.</summary>
    const int MaxAgenda = 200;
}

using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's appointments screen and a leader's schedule (Phase E, 8.7).</summary>
/// <remarks>
/// Every query names the institution: on <c>ServiceAppointments</c> RLS's owner branch would add the
/// caller's own requests as a donor elsewhere (V9). The agenda's filter is one of four fixed SQL
/// texts, chosen in C#; nothing the caller sends is ever written into the SQL.
/// </remarks>
public sealed class AppointmentQueries(IDbConnectionFactory connections) : IAppointmentQueries
{
    private const string LiveLeadersSql =
        """
        (SELECT COUNT(*) FROM booking.AppointmentLeaders AS al
         WHERE al.AppointmentId = a.Id AND al.InstitutionId = a.InstitutionId AND al.Response IN ('Pending', 'Accepted'))
        """;

    private const string AgendaSelectSql =
        $"""
        SELECT TOP (@max)
               a.Id AS Id, a.AppointmentNo AS AppointmentNo, a.Status AS Status, a.StartsAt AS StartsAt, a.EndsAt AS EndsAt,
               t.PublicId AS ServiceType, t.Name AS ServiceName, t.NameSi AS ServiceNameSi, t.NameTa AS ServiceNameTa,
               t.LeadersRequired AS LeadersRequired, a.LocationMode AS LocationMode, a.Address AS Address,
               a.Occasion AS Occasion, a.DonorNote AS DonorNote, a.DonorName AS DonorName, a.DonorPhone AS DonorPhone,
               a.CreatedAt AS CreatedAt, a.RejectReason AS RejectReason, a.CancelReason AS CancelReason,
               {LiveLeadersSql} AS LiveLeaders
        FROM booking.ServiceAppointments AS a
        JOIN booking.ServiceTypes AS t ON t.Id = a.ServiceTypeId AND t.InstitutionId = a.InstitutionId
        WHERE a.InstitutionId = @institutionId
        """;

    private const string UpcomingSql =
        $"""
        {AgendaSelectSql}
          AND a.Status IN ('Requested', 'Approved') AND a.EndsAt > @now
        ORDER BY a.StartsAt, a.Id;
        """;

    private const string WaitingSql =
        $"""
        {AgendaSelectSql}
          AND a.Status = 'Requested' AND a.EndsAt > @now
        ORDER BY a.StartsAt, a.Id;
        """;

    private const string NeedsLeadersSql =
        $"""
        {AgendaSelectSql}
          AND a.Status IN ('Requested', 'Approved') AND a.EndsAt > @now
          AND {LiveLeadersSql} < t.LeadersRequired
        ORDER BY a.StartsAt, a.Id;
        """;

    private const string HistorySql =
        $"""
        {AgendaSelectSql}
          AND (a.EndsAt <= @now OR a.Status IN ('Rejected', 'Cancelled', 'Completed'))
        ORDER BY a.StartsAt DESC, a.Id DESC;
        """;

    // Live leaders first, then those who declined; a leader removed is no longer part of it.
    private const string LeadersSql =
        """
        SELECT al.AppointmentId AS AppointmentId, l.PublicId AS PublicId, l.DisplayName AS DisplayName,
               l.NameSi AS NameSi, l.NameTa AS NameTa, l.Title AS Title, al.Response AS Response,
               al.RespondedOnBehalf AS RespondedOnBehalf, al.DeclineReason AS DeclineReason
        FROM booking.AppointmentLeaders AS al
        JOIN org.ReligiousLeaders AS l ON l.Id = al.LeaderId AND l.InstitutionId = al.InstitutionId
        WHERE al.InstitutionId = @institutionId AND al.AppointmentId IN @ids AND al.Response <> 'Removed'
        ORDER BY CASE WHEN al.Response = 'Declined' THEN 1 ELSE 0 END, l.DisplayName;
        """;

    private const string ScheduleSql =
        """
        SELECT a.AppointmentNo AS AppointmentNo, a.StartsAt AS StartsAt, a.EndsAt AS EndsAt,
               t.PublicId AS ServiceType, t.Name AS ServiceName, t.NameSi AS ServiceNameSi, t.NameTa AS ServiceNameTa,
               a.LocationMode AS LocationMode, a.Address AS Address, a.Occasion AS Occasion,
               a.DonorName AS DonorName, a.DonorPhone AS DonorPhone, al.Response AS Response
        FROM org.ReligiousLeaders AS l
        JOIN booking.AppointmentLeaders AS al ON al.LeaderId = l.Id AND al.InstitutionId = l.InstitutionId
        JOIN booking.ServiceAppointments AS a ON a.Id = al.AppointmentId AND a.InstitutionId = al.InstitutionId
        JOIN booking.ServiceTypes AS t ON t.Id = a.ServiceTypeId AND t.InstitutionId = a.InstitutionId
        WHERE l.UserId = @userId AND l.InstitutionId = @institutionId AND l.IsActive = 1
          AND a.Status = 'Approved' AND a.EndsAt > @now AND al.Response IN ('Pending', 'Accepted')
        ORDER BY a.StartsAt, a.Id;
        """;

    public async Task<IReadOnlyList<AppointmentAgendaView>> AgendaAsync(int institutionId, AppointmentAgenda agenda, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var sql = agenda switch
        {
            AppointmentAgenda.Upcoming => UpcomingSql,
            AppointmentAgenda.Waiting => WaitingSql,
            AppointmentAgenda.NeedsLeaders => NeedsLeadersSql,
            AppointmentAgenda.History => HistorySql,
            _ => throw new ArgumentOutOfRangeException(nameof(agenda)),
        };

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await connection.QueryAsync<AgendaRow>(new CommandDefinition(
            sql,
            new { institutionId, now = utcNow, max = IAppointmentQueries.MaxAgenda },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        IEnumerable<LeaderRow> leaderRows = rows.Count == 0
            ? []
            : await connection.QueryAsync<LeaderRow>(new CommandDefinition(
                LeadersSql,
                new { institutionId, ids = rows.Select(r => r.Id).ToArray() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        var leaders = leaderRows.ToLookup(l => l.AppointmentId);

        return [.. rows.Select(r => new AppointmentAgendaView(
            r.AppointmentNo, r.Status, Utc(r.StartsAt), Utc(r.EndsAt), r.ServiceType, r.ServiceName, r.ServiceNameSi,
            r.ServiceNameTa, r.LeadersRequired, r.LocationMode, r.Address, r.Occasion, r.DonorNote, r.DonorName,
            r.DonorPhone, Utc(r.CreatedAt), r.RejectReason, r.CancelReason,
            Utc(r.EndsAt) > utcNow && ServiceAppointment.NeedsLeaders(r.Status, r.LiveLeaders, r.LeadersRequired),
            [.. leaders[r.Id].Select(l => new AppointmentLeaderView(
                l.PublicId, l.DisplayName, l.NameSi, l.NameTa, l.Title, l.Response, l.RespondedOnBehalf, l.DeclineReason))]))];
    }

    public async Task<IReadOnlyList<LeaderScheduleView>> ScheduleAsync(int institutionId, Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ScheduleRow>(new CommandDefinition(
            ScheduleSql,
            new { institutionId, userId, now = utcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new LeaderScheduleView(
            r.AppointmentNo, Utc(r.StartsAt), Utc(r.EndsAt), r.ServiceType, r.ServiceName, r.ServiceNameSi, r.ServiceNameTa,
            r.LocationMode, r.Address, r.Occasion, r.DonorName, r.DonorPhone, r.Response))];
    }

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);

    private sealed class AgendaRow
    {
        public long Id { get; init; }
        public string AppointmentNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public Guid ServiceType { get; init; }
        public string ServiceName { get; init; } = null!;
        public string? ServiceNameSi { get; init; }
        public string? ServiceNameTa { get; init; }
        public byte LeadersRequired { get; init; }
        public string LocationMode { get; init; } = null!;
        public string? Address { get; init; }
        public string? Occasion { get; init; }
        public string? DonorNote { get; init; }
        public string DonorName { get; init; } = null!;
        public string DonorPhone { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public string? RejectReason { get; init; }
        public string? CancelReason { get; init; }
        public int LiveLeaders { get; init; }
    }

    private sealed class LeaderRow
    {
        public long AppointmentId { get; init; }
        public Guid PublicId { get; init; }
        public string DisplayName { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string? Title { get; init; }
        public string Response { get; init; } = null!;
        public bool RespondedOnBehalf { get; init; }
        public string? DeclineReason { get; init; }
    }

    private sealed class ScheduleRow
    {
        public string AppointmentNo { get; init; } = null!;
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public Guid ServiceType { get; init; }
        public string ServiceName { get; init; } = null!;
        public string? ServiceNameSi { get; init; }
        public string? ServiceNameTa { get; init; }
        public string LocationMode { get; init; } = null!;
        public string? Address { get; init; }
        public string? Occasion { get; init; }
        public string DonorName { get; init; } = null!;
        public string DonorPhone { get; init; } = null!;
        public string Response { get; init; } = null!;
    }
}

using System.Globalization;
using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's services, roster and assign screens (Phase E, 8.4).</summary>
/// <remarks>
/// Every query names the institution. <c>ServiceTypes</c> is readable by anyone (the public page
/// lists them), and on <c>ServiceAppointments</c> the owner branch would add the caller's own
/// requests as a donor elsewhere (V9), so Row-Level Security alone would show too much.
/// </remarks>
public sealed class ServiceSetupQueries(IDbConnectionFactory connections) : IServiceSetupQueries
{
    private const string TypesSql =
        """
        SELECT t.PublicId AS PublicId, t.Name AS Name, t.NameSi AS NameSi, t.NameTa AS NameTa,
               t.Description AS Description, t.DurationMinutes AS DurationMinutes,
               t.BufferMinutes AS BufferMinutes, t.LeadersRequired AS LeadersRequired,
               t.LocationMode AS LocationMode, t.MinAdvanceDays AS MinAdvanceDays,
               t.MaxAdvanceDays AS MaxAdvanceDays, t.IsActive AS IsActive, t.RowVersion AS Version
        FROM booking.ServiceTypes AS t
        WHERE t.InstitutionId = @institutionId
        ORDER BY t.IsActive DESC, t.Name;
        """;

    private const string LeadersSql =
        """
        SELECT l.Id AS Id, l.PublicId AS PublicId, l.DisplayName AS DisplayName, l.NameSi AS NameSi,
               l.NameTa AS NameTa, l.Title AS Title, l.Phone AS Phone, l.Language AS Language,
               CAST(CASE WHEN l.UserId IS NULL THEN 0 ELSE 1 END AS BIT) AS HasAccount,
               l.IsActive AS IsActive, l.RowVersion AS Version
        FROM org.ReligiousLeaders AS l
        WHERE l.InstitutionId = @institutionId
        ORDER BY l.IsActive DESC, l.DisplayName;

        SELECT h.LeaderId AS LeaderId, h.DayOfWeek AS DayOfWeek,
               CONVERT(CHAR(5), h.StartTime, 108) AS StartTime, CONVERT(CHAR(5), h.EndTime, 108) AS EndTime
        FROM org.LeaderWeeklyHours AS h
        WHERE h.InstitutionId = @institutionId
        ORDER BY h.LeaderId, h.DayOfWeek, h.StartTime;
        """;

    private const string TimeOffSql =
        """
        SELECT t.Id AS Id, l.PublicId AS Leader, l.DisplayName AS LeaderName, t.StartsAt AS StartsAt,
               t.EndsAt AS EndsAt, t.Reason AS Reason
        FROM org.LeaderTimeOff AS t
        JOIN org.ReligiousLeaders AS l ON l.Id = t.LeaderId AND l.InstitutionId = t.InstitutionId
        WHERE t.InstitutionId = @institutionId AND t.EndsAt > @utcNow
        ORDER BY t.StartsAt, l.DisplayName, t.Id;
        """;

    private const string AppointmentSql =
        """
        SELECT a.Id AS Id, a.StartsAt AS StartsAt, a.EndsAt AS EndsAt,
               CAST(CASE WHEN a.LocationMode = 'AtDonorPlace' THEN t.BufferMinutes ELSE 0 END AS INT) AS BufferMinutes
        FROM booking.ServiceAppointments AS a
        JOIN booking.ServiceTypes AS t ON t.Id = a.ServiceTypeId AND t.InstitutionId = a.InstitutionId
        WHERE a.AppointmentNo = @appointmentNo AND a.InstitutionId = @institutionId;
        """;

    // The leader's own blocks for this appointment are not "busy": they are the assignment itself.
    // Leave is named before another appointment, since it is the one the admin cannot reassign.
    private const string CandidatesSql =
        """
        SELECT l.Id AS Id, l.PublicId AS PublicId, l.DisplayName AS DisplayName, l.NameSi AS NameSi,
               l.NameTa AS NameTa, l.Title AS Title,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM booking.AppointmentLeaders AS al
                                      WHERE al.AppointmentId = @appointmentId AND al.LeaderId = l.Id
                                        AND al.Response IN ('Pending', 'Accepted'))
                         THEN 1 ELSE 0 END AS BIT) AS Assigned,
               CASE WHEN EXISTS (SELECT 1 FROM booking.LeaderTimeBlocks AS b
                                 WHERE b.LeaderId = l.Id AND b.BlockStart >= @from AND b.BlockStart < @to
                                   AND b.Kind = 'TimeOff')
                    THEN 'TimeOff'
                    WHEN EXISTS (SELECT 1 FROM booking.LeaderTimeBlocks AS b
                                 WHERE b.LeaderId = l.Id AND b.BlockStart >= @from AND b.BlockStart < @to
                                   AND b.Kind = 'Appointment' AND b.AppointmentId <> @appointmentId)
                    THEN 'Appointment'
               END AS Busy
        FROM org.ReligiousLeaders AS l
        WHERE l.InstitutionId = @institutionId AND l.IsActive = 1;

        SELECT h.LeaderId AS LeaderId, h.DayOfWeek AS DayOfWeek,
               CONVERT(CHAR(5), h.StartTime, 108) AS StartTime, CONVERT(CHAR(5), h.EndTime, 108) AS EndTime
        FROM org.LeaderWeeklyHours AS h
        JOIN org.ReligiousLeaders AS l ON l.Id = h.LeaderId AND l.InstitutionId = h.InstitutionId
        WHERE h.InstitutionId = @institutionId AND l.IsActive = 1;
        """;

    public async Task<IReadOnlyList<ServiceTypeView>> ListTypesAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ServiceTypeView>(new CommandDefinition(
            TypesSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<IReadOnlyList<LeaderView>> ListLeadersAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            LeadersSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var leaders = (await results.ReadAsync<LeaderRow>().ConfigureAwait(false)).ToList();
        var hours = (await results.ReadAsync<HoursRow>().ConfigureAwait(false)).ToLookup(h => h.LeaderId);

        return [.. leaders.Select(l => new LeaderView(
            l.PublicId, l.DisplayName, l.NameSi, l.NameTa, l.Title, l.Phone, l.Language, l.HasAccount, l.IsActive, l.Version,
            [.. hours[l.Id].Select(h => new WeeklyHoursView(h.DayOfWeek, h.StartTime, h.EndTime))]))];
    }

    public async Task<IReadOnlyList<TimeOffView>> ListTimeOffAsync(int institutionId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<TimeOffRow>(new CommandDefinition(
            TimeOffSql,
            new { institutionId, utcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new TimeOffView(
            r.Id, r.Leader, r.LeaderName, DateTime.SpecifyKind(r.StartsAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.EndsAt, DateTimeKind.Utc), r.Reason))];
    }

    public async Task<IReadOnlyList<LeaderCandidateView>?> LeaderCandidatesAsync(int institutionId, string appointmentNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var appointment = await connection.QuerySingleOrDefaultAsync<AppointmentRow>(new CommandDefinition(
            AppointmentSql,
            new { institutionId, appointmentNo = appointmentNo.Trim() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (appointment is null)
        {
            return null;
        }

        // The same span the assignment would take (TimeBlocks.For), as a half-open range.
        var startsAt = DateTime.SpecifyKind(appointment.StartsAt, DateTimeKind.Utc);
        var endsAt = DateTime.SpecifyKind(appointment.EndsAt, DateTimeKind.Utc);
        var blocks = TimeBlocks.For(startsAt, endsAt, appointment.BufferMinutes);
        var from = blocks[0];
        var to = blocks[^1].AddMinutes(TimeBlocks.Minutes);

        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            CandidatesSql,
            new { institutionId, appointmentId = appointment.Id, from, to },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var leaders = (await results.ReadAsync<CandidateRow>().ConfigureAwait(false)).ToList();
        var hours = (await results.ReadAsync<HoursRow>().ConfigureAwait(false)).ToLookup(h => h.LeaderId);

        // The usual week is about the leader, not the travel: the service's own time, in Colombo.
        var colomboStart = SriLankaTime.FromUtc(startsAt);
        var colomboEnd = SriLankaTime.FromUtc(endsAt);

        return [.. leaders
            .Select(l =>
            {
                var week = hours[l.Id].ToList();
                bool? available = week.Count == 0
                    ? null
                    : week.Exists(h => new LeaderWeeklyHours
                    {
                        DayOfWeek = h.DayOfWeek,
                        StartTime = TimeOnly.ParseExact(h.StartTime, "HH:mm", CultureInfo.InvariantCulture),
                        EndTime = TimeOnly.ParseExact(h.EndTime, "HH:mm", CultureInfo.InvariantCulture),
                    }.Covers(colomboStart, colomboEnd));

                return new LeaderCandidateView(l.PublicId, l.DisplayName, l.NameSi, l.NameTa, l.Title, l.Assigned, l.Busy, available);
            })
            .OrderByDescending(c => c.Assigned)
            .ThenBy(c => c.Busy is not null)
            .ThenBy(c => c.NormallyAvailable switch { true => 0, null => 1, false => 2 })
            .ThenBy(c => c.DisplayName, StringComparer.Ordinal)];
    }

    private sealed class LeaderRow
    {
        public int Id { get; init; }
        public Guid PublicId { get; init; }
        public string DisplayName { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string? Title { get; init; }
        public string? Phone { get; init; }
        public string Language { get; init; } = null!;
        public bool HasAccount { get; init; }
        public bool IsActive { get; init; }
        public byte[] Version { get; init; } = [];
    }

    private sealed class HoursRow
    {
        public int LeaderId { get; init; }
        public byte DayOfWeek { get; init; }
        public string StartTime { get; init; } = null!;
        public string EndTime { get; init; } = null!;
    }

    private sealed class TimeOffRow
    {
        public int Id { get; init; }
        public Guid Leader { get; init; }
        public string LeaderName { get; init; } = null!;
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public string? Reason { get; init; }
    }

    private sealed class AppointmentRow
    {
        public long Id { get; init; }
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public int BufferMinutes { get; init; }
    }

    private sealed class CandidateRow
    {
        public int Id { get; init; }
        public Guid PublicId { get; init; }
        public string DisplayName { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string? Title { get; init; }
        public bool Assigned { get; init; }
        public string? Busy { get; init; }
    }
}

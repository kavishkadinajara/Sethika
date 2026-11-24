using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the appointment texts (Phase E, 8.8), run as the platform.</summary>
/// <remarks>
/// <c>org.ReligiousLeaders.Phone</c> and <c>auth.Users.PhoneNumber</c> are masked, so the login this
/// runs under needs <c>UNMASK</c> on both before deployment, as for the booking texts (plan 02, B12).
/// </remarks>
public sealed class AppointmentNotificationQueries(IDbConnectionFactory connections) : IAppointmentNotificationQueries
{
    private const string Select =
        """
        SELECT a.AppointmentNo AS AppointmentNo, a.Status AS Status, a.DonorUserId AS DonorUserId,
               u.PreferredLanguage AS Language, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, a.StartsAt AS StartsAt
        FROM booking.ServiceAppointments AS a
        JOIN org.Institutions AS i ON i.Id = a.InstitutionId
        LEFT JOIN auth.Users AS u ON u.Id = a.DonorUserId
        """;

    /// <summary><c>booking.ServiceAppointments.AppointmentNo</c> is <c>VARCHAR(20)</c>; sent as such, its unique index is seeked.</summary>
    private const int AppointmentNoLength = 20;

    private const string OneSql = Select + "\nWHERE a.AppointmentNo = @appointmentNo;";

    private const string ApprovedStartingSql =
        Select + "\nWHERE a.Status = 'Approved' AND a.StartsAt >= @fromUtc AND a.StartsAt < @toUtc\nORDER BY a.StartsAt, a.Id;";

    // A linked account's confirmed number stands in when the roster has none; its language always wins (V14).
    private const string LeadersSql =
        """
        SELECT l.Id AS LeaderId,
               COALESCE(l.Phone, CASE WHEN u.PhoneConfirmed = 1 AND u.IsDeleted = 0 THEN u.PhoneNumber END) AS Phone,
               COALESCE(u.PreferredLanguage, l.Language) AS Language,
               al.Response AS Response
        FROM booking.ServiceAppointments AS a
        JOIN booking.AppointmentLeaders AS al ON al.AppointmentId = a.Id AND al.InstitutionId = a.InstitutionId
        JOIN org.ReligiousLeaders AS l ON l.Id = al.LeaderId AND l.InstitutionId = al.InstitutionId
        LEFT JOIN auth.Users AS u ON u.Id = l.UserId
        WHERE a.AppointmentNo = @appointmentNo
        ORDER BY l.Id;
        """;

    public async Task<AppointmentTextDetails?> GetAsync(string appointmentNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            OneSql,
            new { appointmentNo = Number(appointmentNo) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDetails();
    }

    public async Task<IReadOnlyList<LeaderTextRecipient>> LeadersAsync(string appointmentNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<LeaderTextRecipient>(new CommandDefinition(
            LeadersSql,
            new { appointmentNo = Number(appointmentNo) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    public async Task<IReadOnlyList<AppointmentTextDetails>> ListApprovedStartingAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            ApprovedStartingSql,
            new { fromUtc, toUtc },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDetails())];
    }

    private static DbString Number(string appointmentNo) => new() { Value = appointmentNo, IsAnsi = true, Length = AppointmentNoLength };

    private sealed class Row
    {
        public string AppointmentNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public Guid DonorUserId { get; init; }
        public string? Language { get; init; }
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public DateTime StartsAt { get; init; }

        public AppointmentTextDetails ToDetails() =>
            new(AppointmentNo, Status, DonorUserId, Language, InstitutionName, InstitutionNameSi, InstitutionNameTa,
                DateTime.SpecifyKind(StartsAt, DateTimeKind.Utc));
    }
}

using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the donor's service requests (Phase E, 8.5).</summary>
/// <remarks>
/// The list names the donor, as well as RLS's owner branch (V9): a staff member signed in for an
/// institution would otherwise see its appointments too.
/// </remarks>
public sealed class DonorAppointmentQueries(IDbConnectionFactory connections) : IDonorAppointmentQueries
{
    private const string MineSql =
        """
        SELECT TOP (@max)
               a.AppointmentNo AS AppointmentNo, a.Status AS Status, a.StartsAt AS StartsAt, a.EndsAt AS EndsAt,
               i.Slug AS Institution, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, t.PublicId AS ServiceType, t.Name AS ServiceName,
               t.NameSi AS ServiceNameSi, t.NameTa AS ServiceNameTa, a.LocationMode AS LocationMode,
               a.Address AS Address, a.Occasion AS Occasion, a.DonorNote AS Note, a.CreatedAt AS CreatedAt,
               a.RejectReason AS RejectReason, a.CancelReason AS CancelReason
        FROM booking.ServiceAppointments AS a
        JOIN booking.ServiceTypes AS t ON t.Id = a.ServiceTypeId AND t.InstitutionId = a.InstitutionId
        JOIN org.Institutions AS i ON i.Id = a.InstitutionId
        WHERE a.DonorUserId = @donorId
        ORDER BY a.StartsAt DESC, a.CreatedAt DESC;
        """;

    public async Task<IReadOnlyList<DonorAppointmentView>> ListMineAsync(Guid donorId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MineRow>(new CommandDefinition(
            MineSql,
            new { donorId, max = IDonorAppointmentQueries.MaxMine },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new DonorAppointmentView(
            r.AppointmentNo, r.Status, Utc(r.StartsAt), Utc(r.EndsAt), r.Institution, r.InstitutionName,
            r.InstitutionNameSi, r.InstitutionNameTa, r.ServiceType, r.ServiceName, r.ServiceNameSi, r.ServiceNameTa,
            r.LocationMode, r.Address, r.Occasion, r.Note, Utc(r.CreatedAt), r.RejectReason, r.CancelReason,
            ServiceAppointment.DonorMayCancel(r.Status, Utc(r.StartsAt), utcNow)))];
    }

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);

    private sealed class MineRow
    {
        public string AppointmentNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public string Institution { get; init; } = null!;
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public Guid ServiceType { get; init; }
        public string ServiceName { get; init; } = null!;
        public string? ServiceNameSi { get; init; }
        public string? ServiceNameTa { get; init; }
        public string LocationMode { get; init; } = null!;
        public string? Address { get; init; }
        public string? Occasion { get; init; }
        public string? Note { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? RejectReason { get; init; }
        public string? CancelReason { get; init; }
    }
}

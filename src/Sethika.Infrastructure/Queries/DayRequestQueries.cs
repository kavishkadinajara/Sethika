using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads of day requests (plan 08, K6) in the caller's own session.</summary>
/// <remarks>
/// Row-Level Security shows a request to its institution and to the donor who asked
/// (<c>sec.fn_TenantOrOwner</c>); each query also names whose requests it wants, so a member who is
/// also a donor somewhere sees only the list asked for.
/// </remarks>
public sealed class DayRequestQueries(IDbConnectionFactory connections) : IDayRequestQueries
{
    private const string InstitutionSql =
        """
        SELECT TOP (@max) r.RequestNo AS RequestNo, r.Status AS Status, r.RequestedDate AS Date, m.PublicId AS MealSlot,
               m.Name AS MealSlotName, u.FullName AS DonorName, u.PhoneNumber AS DonorPhone, r.Headcount AS Headcount,
               r.FulfilmentMode AS Fulfilment, r.Note AS Note, r.CreatedAt AS CreatedAt, b.BookingNo AS BookingNo,
               r.DeclineReason AS DeclineReason, r.MealSlotId AS MealSlotId
        FROM booking.DayRequests AS r
        JOIN booking.MealSlots AS m ON m.Id = r.MealSlotId AND m.InstitutionId = r.InstitutionId
        JOIN auth.Users AS u ON u.Id = r.DonorUserId
        LEFT JOIN booking.Bookings AS b ON b.Id = r.BookingId AND b.InstitutionId = r.InstitutionId
        WHERE r.InstitutionId = @institutionId AND (@all = 1 OR r.Status = 'Open')
        ORDER BY CASE WHEN r.Status = 'Open' THEN 0 ELSE 1 END,
                 CASE WHEN r.Status = 'Open' THEN r.CreatedAt END,
                 r.CreatedAt DESC, r.Id DESC;

        SELECT si.MealSlotId AS MealSlotId, si.SlotDate AS Date, s.PublicId AS Sponsorship, d.FullName AS DonorName
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        JOIN booking.StandingSponsorships AS s ON s.Id = b.StandingSponsorshipId AND s.InstitutionId = b.InstitutionId
        JOIN org.InstitutionDonors AS d ON d.Id = s.InstitutionDonorId AND d.InstitutionId = s.InstitutionId
        WHERE b.InstitutionId = @institutionId AND b.Source = 'Standing' AND b.Status IN ('Requested', 'Approved')
          AND EXISTS (SELECT 1 FROM booking.DayRequests AS r
                      WHERE r.InstitutionId = b.InstitutionId AND r.Status = 'Open'
                        AND r.MealSlotId = si.MealSlotId AND r.RequestedDate = si.SlotDate)
        ORDER BY b.Id;
        """;

    private const string MineSql =
        """
        SELECT TOP (@max) r.RequestNo AS RequestNo, r.Status AS Status, r.RequestedDate AS Date, i.Slug AS Institution,
               i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa,
               m.PublicId AS MealSlot, m.Name AS MealSlotName, r.Headcount AS Headcount, r.FulfilmentMode AS Fulfilment,
               r.CreatedAt AS CreatedAt, b.BookingNo AS BookingNo, r.DeclineReason AS DeclineReason
        FROM booking.DayRequests AS r
        JOIN booking.MealSlots AS m ON m.Id = r.MealSlotId AND m.InstitutionId = r.InstitutionId
        JOIN org.Institutions AS i ON i.Id = r.InstitutionId
        LEFT JOIN booking.Bookings AS b ON b.Id = r.BookingId AND b.InstitutionId = r.InstitutionId
        WHERE r.DonorUserId = @donorId
        ORDER BY r.CreatedAt DESC, r.Id DESC;
        """;

    public async Task<IReadOnlyList<InstitutionDayRequestView>> ListForInstitutionAsync(int institutionId, bool all, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            InstitutionSql,
            new { institutionId, all, max = IDayRequestQueries.MaxList },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var requests = (await reader.ReadAsync<InstitutionRow>().ConfigureAwait(false)).ToList();
        var holders = (await reader.ReadAsync<HolderRow>().ConfigureAwait(false))
            .ToLookup(h => (h.MealSlotId, h.Date), h => new DayHolder(h.Sponsorship, h.DonorName));

        return [.. requests.Select(r => new InstitutionDayRequestView(
            r.RequestNo, r.Status, r.Date, r.MealSlot, r.MealSlotName, r.DonorName, r.DonorPhone, r.Headcount, r.Fulfilment,
            r.Note, r.CreatedAt, r.BookingNo, r.DeclineReason,
            r.Status == "Open" ? [.. holders[(r.MealSlotId, r.Date)]] : []))];
    }

    public async Task<IReadOnlyList<MyDayRequestView>> ListMineAsync(Guid donorId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MineRow>(new CommandDefinition(
            MineSql,
            new { donorId, max = IDayRequestQueries.MaxList },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new MyDayRequestView(
            r.RequestNo, r.Status, r.Date, r.Institution, r.InstitutionName, r.InstitutionNameSi, r.InstitutionNameTa,
            r.MealSlot, r.MealSlotName, r.Headcount, r.Fulfilment, r.CreatedAt, r.BookingNo, r.DeclineReason))];
    }

    // Row classes rather than the records: Dapper's constructor matching does not apply the DateOnly handler.
    private sealed class InstitutionRow
    {
        public string RequestNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateOnly Date { get; init; }
        public Guid MealSlot { get; init; }
        public string MealSlotName { get; init; } = null!;
        public string DonorName { get; init; } = null!;
        public string? DonorPhone { get; init; }
        public short Headcount { get; init; }
        public string Fulfilment { get; init; } = null!;
        public string? Note { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? BookingNo { get; init; }
        public string? DeclineReason { get; init; }
        public int MealSlotId { get; init; }
    }

    private sealed class HolderRow
    {
        public int MealSlotId { get; init; }
        public DateOnly Date { get; init; }
        public Guid Sponsorship { get; init; }
        public string DonorName { get; init; } = null!;
    }

    private sealed class MineRow
    {
        public string RequestNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateOnly Date { get; init; }
        public string Institution { get; init; } = null!;
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public Guid MealSlot { get; init; }
        public string MealSlotName { get; init; } = null!;
        public short Headcount { get; init; }
        public string Fulfilment { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public string? BookingNo { get; init; }
        public string? DeclineReason { get; init; }
    }
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's booking screens (plan 03, 5.4).</summary>
/// <remarks>
/// Every query names the institution. Row-Level Security would not be enough on its own:
/// <c>MealSlots</c>, <c>BlockedDates</c> and <c>SlotInstances</c> are readable by anyone
/// (availability is public, C8), and on <c>Bookings</c> the owner branch would add the caller's own
/// bookings as a donor at other institutions (§8, E8).
/// </remarks>
public sealed class InstitutionBookingQueries(IDbConnectionFactory connections) : IInstitutionBookingQueries
{
    private const string SlotsSql =
        """
        SELECT s.PublicId AS PublicId, s.Name AS Name, s.NameSi AS NameSi, s.NameTa AS NameTa,
               CONVERT(CHAR(5), s.StartTime, 108) AS StartTime, CONVERT(CHAR(5), s.EndTime, 108) AS EndTime,
               s.DaysOfWeekMask AS DaysOfWeekMask, s.BookingMode AS BookingMode, s.MaxSponsors AS MaxSponsors,
               s.MinHeadcount AS MinHeadcount, s.MaxHeadcount AS MaxHeadcount,
               s.EstimatedCostPerHead AS EstimatedCostPerHead, s.MinAdvanceDays AS MinAdvanceDays,
               s.MaxAdvanceDays AS MaxAdvanceDays, s.RequiresApproval AS RequiresApproval,
               s.AllowedFulfilment AS AllowedFulfilment, s.SortOrder AS SortOrder, s.IsActive AS IsActive,
               s.RowVersion AS Version
        FROM booking.MealSlots AS s
        WHERE s.InstitutionId = @institutionId
        ORDER BY s.IsActive DESC, s.SortOrder, s.StartTime, s.Name;
        """;

    private const string BlockedDatesSql =
        """
        SELECT b.Id AS Id, b.DateFrom AS DateFrom, b.DateTo AS DateTo, s.PublicId AS MealSlot,
               s.Name AS MealSlotName, b.Reason AS Reason
        FROM booking.BlockedDates AS b
        LEFT JOIN booking.MealSlots AS s ON s.Id = b.MealSlotId AND s.InstitutionId = b.InstitutionId
        WHERE b.InstitutionId = @institutionId AND b.DateTo >= @from
        ORDER BY b.DateFrom, b.Id;
        """;

    private const string ListingSql =
        """
        SELECT i.Slug AS Slug, i.IsPubliclyListed AS Listed
        FROM org.Institutions AS i
        WHERE i.Id = @institutionId;
        """;

    // One row per booking, or one per slot-day with no booking to show when no status is asked for.
    // A slot-day exists only once it has been booked (C3), so an unfiltered day always has bookings.
    private const string CalendarSql =
        """
        SELECT si.SlotDate AS Date, s.PublicId AS MealSlot, s.Name AS SlotName, s.NameSi AS SlotNameSi,
               s.NameTa AS SlotNameTa, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime,
               CONVERT(CHAR(5), s.EndTime, 108) AS EndTime, si.MaxSponsors AS MaxSponsors,
               si.BookedSponsors AS BookedSponsors, si.MaxHeadcount AS MaxHeadcount,
               si.BookedHeadcount AS BookedHeadcount, si.Status AS SlotDayStatus,
               b.BookingNo AS BookingNo, b.Status AS Status, b.DonorName AS DonorName, b.DonorPhone AS DonorPhone,
               b.Headcount AS Headcount, b.FulfilmentMode AS FulfilmentMode, b.InMemoryOf AS InMemoryOf,
               b.Notes AS Notes, b.CreatedAt AS CreatedAt, b.RejectReason AS RejectReason,
               b.CancelReason AS CancelReason
        FROM booking.SlotInstances AS si
        JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
        LEFT JOIN booking.Bookings AS b
          ON b.SlotInstanceId = si.Id AND b.InstitutionId = si.InstitutionId
         AND (@status IS NULL OR b.Status = @status)
        WHERE si.InstitutionId = @institutionId
          AND si.SlotDate >= @first AND si.SlotDate <= @last
          AND (@status IS NULL OR b.Id IS NOT NULL)
        ORDER BY si.SlotDate, s.StartTime, s.SortOrder, s.Name, b.CreatedAt, b.BookingNo;
        """;

    public async Task<IReadOnlyList<InstitutionMealSlotView>> ListSlotsAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SlotRow>(new CommandDefinition(
            SlotsSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new InstitutionMealSlotView(
            r.PublicId, r.Name, r.NameSi, r.NameTa, r.StartTime, r.EndTime, r.DaysOfWeekMask, r.BookingMode,
            r.MaxSponsors, r.MinHeadcount, r.MaxHeadcount, r.EstimatedCostPerHead, r.MinAdvanceDays,
            r.MaxAdvanceDays, r.RequiresApproval,
            r.AllowedFulfilment.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            r.SortOrder, r.IsActive, r.Version))];
    }

    public async Task<IReadOnlyList<BlockedDateView>> ListBlockedDatesAsync(int institutionId, DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<BlockedDateRow>(new CommandDefinition(
            BlockedDatesSql,
            new { institutionId, from },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Through a row class: Dapper's constructor matching does not apply the DateOnly handler.
        return [.. rows.Select(r => new BlockedDateView(r.Id, r.DateFrom, r.DateTo, r.MealSlot, r.MealSlotName, r.Reason))];
    }

    public async Task<InstitutionListingView> GetListingAsync(int institutionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync<InstitutionListingView>(new CommandDefinition(
            ListingSql,
            new { institutionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SlotDayView>> GetCalendarAsync(
        int institutionId,
        DateOnly first,
        DateOnly last,
        string? status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CalendarRow>(new CommandDefinition(
            CalendarSql,
            new { institutionId, first, last, status },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Ordered by slot-day, so a slot-day's rows are consecutive and keep their booking order.
        return [.. rows
            .GroupBy(r => (r.Date, r.MealSlot))
            .Select(day =>
            {
                var first = day.First();
                return new SlotDayView(
                    first.Date, first.MealSlot, first.SlotName, first.SlotNameSi, first.SlotNameTa,
                    first.StartTime, first.EndTime, first.MaxSponsors, first.BookedSponsors,
                    first.MaxHeadcount, first.BookedHeadcount, first.SlotDayStatus,
                    [.. day
                        .Where(r => r.BookingNo is not null)
                        .Select(r => new CalendarBookingView(
                            r.BookingNo!, r.Status!, r.DonorName!, r.DonorPhone!, r.Headcount!.Value,
                            r.FulfilmentMode!, r.InMemoryOf, r.Notes, r.CreatedAt!.Value, r.RejectReason,
                            r.CancelReason))]);
            })];
    }

    private sealed class SlotRow
    {
        public Guid PublicId { get; init; }
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public string StartTime { get; init; } = null!;
        public string EndTime { get; init; } = null!;
        public byte DaysOfWeekMask { get; init; }
        public string BookingMode { get; init; } = null!;
        public byte MaxSponsors { get; init; }
        public short MinHeadcount { get; init; }
        public short MaxHeadcount { get; init; }
        public decimal? EstimatedCostPerHead { get; init; }
        public short MinAdvanceDays { get; init; }
        public short MaxAdvanceDays { get; init; }
        public bool RequiresApproval { get; init; }
        public string AllowedFulfilment { get; init; } = null!;
        public short SortOrder { get; init; }
        public bool IsActive { get; init; }
        public byte[] Version { get; init; } = [];
    }

    private sealed class BlockedDateRow
    {
        public int Id { get; init; }
        public DateOnly DateFrom { get; init; }
        public DateOnly DateTo { get; init; }
        public Guid? MealSlot { get; init; }
        public string? MealSlotName { get; init; }
        public string Reason { get; init; } = null!;
    }

    private sealed class CalendarRow
    {
        public DateOnly Date { get; init; }
        public Guid MealSlot { get; init; }
        public string SlotName { get; init; } = null!;
        public string? SlotNameSi { get; init; }
        public string? SlotNameTa { get; init; }
        public string StartTime { get; init; } = null!;
        public string EndTime { get; init; } = null!;
        public byte MaxSponsors { get; init; }
        public byte BookedSponsors { get; init; }
        public short MaxHeadcount { get; init; }
        public short BookedHeadcount { get; init; }
        public string SlotDayStatus { get; init; } = null!;
        public string? BookingNo { get; init; }
        public string? Status { get; init; }
        public string? DonorName { get; init; }
        public string? DonorPhone { get; init; }
        public short? Headcount { get; init; }
        public string? FulfilmentMode { get; init; }
        public string? InMemoryOf { get; init; }
        public string? Notes { get; init; }
        public DateTime? CreatedAt { get; init; }
        public string? RejectReason { get; init; }
        public string? CancelReason { get; init; }
    }
}

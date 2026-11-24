using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's standing sponsorships (plan 08, K2).</summary>
/// <remarks>
/// Row-Level Security keeps sponsorships, their exceptions and the donor book to their institution
/// (<c>sec.fn_Tenant</c>); the queries name the institution all the same, so a platform connection,
/// which sees every row, still reads one.
/// </remarks>
public sealed class StandingSponsorshipQueries(IDbConnectionFactory connections) : IStandingSponsorshipQueries
{
    private const string Select =
        """
        SELECT s.PublicId AS PublicId, d.PublicId AS Donor, d.FullName AS DonorName,
               m.PublicId AS MealSlot, m.Name AS MealSlotName, s.Pattern AS Pattern, s.MonthInterval AS MonthInterval,
               s.WeekDays AS WeekDays, s.MonthDays AS MonthDays, s.YearMonth AS YearMonth, s.YearDay AS YearDay,
               s.StartDate AS StartDate, s.EndDate AS EndDate, s.Headcount AS Headcount, s.FulfilmentMode AS Fulfilment,
               s.InMemoryOf AS InMemoryOf, s.Notes AS Notes, s.Status AS Status, s.EndedAt AS EndedAt,
               m.MaxAdvanceDays AS MaxAdvanceDays, s.RowVersion AS Version
        FROM booking.StandingSponsorships AS s
        JOIN org.InstitutionDonors AS d ON d.Id = s.InstitutionDonorId AND d.InstitutionId = s.InstitutionId
        JOIN booking.MealSlots AS m     ON m.Id = s.MealSlotId AND m.InstitutionId = s.InstitutionId
        WHERE s.InstitutionId = @institutionId
        """;

    private const string ListSql =
        Select + "\n  AND (@donor IS NULL OR d.PublicId = @donor)\nORDER BY CASE WHEN s.Status = 'Active' THEN 0 ELSE 1 END, s.Id DESC;";

    private const string OneSql =
        Select + "\n  AND s.PublicId = @publicId;\n" +
        """
        SELECT si.SlotDate AS Date, b.BookingNo AS BookingNo, b.Status AS Status
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId
        JOIN booking.StandingSponsorships AS s ON s.Id = b.StandingSponsorshipId
        WHERE s.PublicId = @publicId AND s.InstitutionId = @institutionId AND si.SlotDate >= @from
        ORDER BY si.SlotDate, b.Id;

        SELECT e.ExceptionDate AS Date, e.Kind AS Kind, e.OtherDate AS OtherDate, e.Reason AS Reason
        FROM booking.StandingExceptions AS e
        JOIN booking.StandingSponsorships AS s ON s.Id = e.StandingSponsorshipId
        WHERE s.PublicId = @publicId AND s.InstitutionId = @institutionId AND e.ExceptionDate >= @from
        ORDER BY e.ExceptionDate;
        """;

    public async Task<IReadOnlyList<SponsorshipView>> ListAsync(int institutionId, Guid? donor, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SponsorshipRow>(new CommandDefinition(
            ListSql,
            new { institutionId, donor },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToView())];
    }

    public async Task<SponsorshipRecord?> GetAsync(int institutionId, Guid publicId, DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            OneSql,
            new { institutionId, publicId, from },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var row = await grid.ReadSingleOrDefaultAsync<SponsorshipRow>().ConfigureAwait(false);
        var bookings = await grid.ReadAsync<BookingRow>().ConfigureAwait(false);
        var exceptions = await grid.ReadAsync<ExceptionRow>().ConfigureAwait(false);

        return row is null
            ? null
            : new SponsorshipRecord(
                row.ToView(),
                [.. bookings.Select(b => new SponsorshipBooking(b.Date, b.BookingNo, b.Status))],
                [.. exceptions.Select(e => new SponsorshipDayChange(e.Date, e.Kind, e.OtherDate, e.Reason))]);
    }

    // Row classes rather than records: Dapper's constructor matching does not apply the DateOnly handler.
    private sealed class SponsorshipRow
    {
        public Guid PublicId { get; init; }
        public Guid Donor { get; init; }
        public string DonorName { get; init; } = null!;
        public Guid MealSlot { get; init; }
        public string MealSlotName { get; init; } = null!;
        public string Pattern { get; init; } = null!;
        public byte MonthInterval { get; init; }
        public byte? WeekDays { get; init; }
        public int? MonthDays { get; init; }
        public byte? YearMonth { get; init; }
        public byte? YearDay { get; init; }
        public DateOnly StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public short Headcount { get; init; }
        public string Fulfilment { get; init; } = null!;
        public string? InMemoryOf { get; init; }
        public string? Notes { get; init; }
        public string Status { get; init; } = null!;
        public DateTime? EndedAt { get; init; }
        public short MaxAdvanceDays { get; init; }
        public byte[] Version { get; init; } = [];

        public SponsorshipView ToView() => new(
            PublicId, Donor, DonorName, MealSlot, MealSlotName, Pattern, MonthInterval,
            StandingPattern.WeekDaysOf(WeekDays), StandingPattern.MonthDaysOf(MonthDays), YearMonth, YearDay,
            StartDate, EndDate, Headcount, Fulfilment, InMemoryOf, Notes, Status,
            EndedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            MaxAdvanceDays, Version);
    }

    private sealed class BookingRow
    {
        public DateOnly Date { get; init; }
        public string BookingNo { get; init; } = null!;
        public string Status { get; init; } = null!;
    }

    private sealed class ExceptionRow
    {
        public DateOnly Date { get; init; }
        public string Kind { get; init; } = null!;
        public DateOnly? OtherDate { get; init; }
        public string? Reason { get; init; }
    }
}

using System.Reflection;
using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the institution's reports (plan 09, M16, M18).</summary>
public sealed class ReportQueries(IDbConnectionFactory connections) : IReportQueries
{
    /// <summary>How far ahead "this week" looks from today: seven days, today included.</summary>
    private const int WeekAhead = 6;

    private static readonly string MonthlySql = ReadSql("MonthlyReport.sql");

    private const string SummarySql =
        """
        SELECT COALESCE(SUM(d.Amount), 0) AS Money
        FROM giving.Donations AS d
        WHERE d.InstitutionId = @institutionId AND d.DonatedAt >= @from AND d.DonatedAt < @to
          AND d.Kind = 'Money' AND d.IsVoided = 0;

        SELECT COUNT(*) AS OpenNeeds
        FROM giving.Needs AS n
        WHERE n.InstitutionId = @institutionId AND n.Status = 'Open';

        SELECT COUNT(*) AS PledgesDue
        FROM giving.Pledges AS p
        WHERE p.InstitutionId = @institutionId AND p.Status = 'Pledged' AND p.ExpectedDeliveryDate <= @weekEnd;
        """;

    public async Task<MonthlyReportData?> GetMonthlyAsync(int institutionId, DateOnly first, CancellationToken cancellationToken = default)
    {
        var last = first.AddMonths(1).AddDays(-1);
        var (from, to) = MonthInstants(first);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            MonthlySql,
            new { institutionId, first, last, from, to },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var institution = await reader.ReadSingleOrDefaultAsync<InstitutionRow>().ConfigureAwait(false);
        var bookings = await reader.ReadAsync<ReportStatusCount>().ConfigureAwait(false);
        var slots = await reader.ReadAsync<SlotRow>().ConfigureAwait(false);
        var booked = await reader.ReadAsync<BookedDayRow>().ConfigureAwait(false);
        var money = await reader.ReadAsync<ReportMethodTotal>().ConfigureAwait(false);
        var inKind = await reader.ReadAsync<ReportCategoryTotal>().ConfigureAwait(false);
        var voided = await reader.ReadAsync<VoidedRow>().ConfigureAwait(false);
        var needs = await reader.ReadSingleAsync<ReportNeedCounts>().ConfigureAwait(false);

        if (institution is null)
        {
            return null;
        }

        return new MonthlyReportData(
            institution.Slug,
            institution.Name,
            institution.NameSi,
            institution.NameTa,
            [.. bookings],
            [.. slots.Select(s => new ReportSlot(s.MealSlot, s.Name, s.NameSi, s.NameTa, Utc(s.CreatedAt)))],
            booked.GroupBy(b => b.MealSlot).ToDictionary(g => g.Key, IReadOnlySet<DateOnly> (g) => g.Select(b => b.SlotDate).ToHashSet()),
            [.. money],
            [.. inKind],
            [.. voided.Select(v => new ReportVoidedGift(
                v.PublicId, v.ReceiptNo, Utc(v.DonatedAt), v.DonorName, v.Kind, v.Amount, v.Items, v.Quantity,
                v.VoidReason, Utc(v.VoidedAt), v.VoidedByName))],
            needs);
    }

    public async Task<GivingSummaryView> GetGivingSummaryAsync(int institutionId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var (from, to) = MonthInstants(new DateOnly(today.Year, today.Month, 1));
        var weekEnd = today.AddDays(WeekAhead);

        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition(
            SummarySql,
            new { institutionId, from, to, weekEnd },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var money = await reader.ReadSingleAsync<decimal>().ConfigureAwait(false);
        var openNeeds = await reader.ReadSingleAsync<int>().ConfigureAwait(false);
        var pledgesDue = await reader.ReadSingleAsync<int>().ConfigureAwait(false);

        return new GivingSummaryView(money, openNeeds, pledgesDue);
    }

    /// <summary>The Colombo month starting <paramref name="first"/> as UTC instants, the end exclusive.</summary>
    private static (DateTime From, DateTime To) MonthInstants(DateOnly first) =>
        (SriLankaTime.ToUtc(first, TimeOnly.MinValue), SriLankaTime.ToUtc(first.AddMonths(1), TimeOnly.MinValue));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string ReadSql(string file)
    {
        var name = $"{typeof(ReportQueries).Namespace}.Sql.{file}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded query {name} is missing.");
        using var text = new StreamReader(stream);
        return text.ReadToEnd();
    }

    private sealed class InstitutionRow
    {
        public string Slug { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
    }

    private sealed class SlotRow
    {
        public Guid MealSlot { get; init; }
        public string Name { get; init; } = null!;
        public string? NameSi { get; init; }
        public string? NameTa { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class BookedDayRow
    {
        public Guid MealSlot { get; init; }
        public DateOnly SlotDate { get; init; }
    }

    private sealed class VoidedRow
    {
        public Guid PublicId { get; init; }
        public string ReceiptNo { get; init; } = null!;
        public DateTime DonatedAt { get; init; }
        public string DonorName { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public decimal? Amount { get; init; }
        public string? Items { get; init; }
        public decimal? Quantity { get; init; }
        public string VoidReason { get; init; } = null!;
        public DateTime VoidedAt { get; init; }
        public string VoidedByName { get; init; } = null!;
    }
}

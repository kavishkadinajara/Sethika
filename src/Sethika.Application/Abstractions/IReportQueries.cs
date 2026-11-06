namespace Sethika.Application.Abstractions;

/// <summary>Bookings of one status in a month's meals.</summary>
public sealed record ReportStatusCount(string Status, int Count);

/// <summary>A slot the report covers.</summary>
/// <param name="CreatedAt">UTC: the slot served no day before it existed (M37).</param>
public sealed record ReportSlot(Guid MealSlot, string Name, string? NameSi, string? NameTa, DateTime CreatedAt);

/// <summary>Money received by one method; voided gifts are not in it.</summary>
public sealed record ReportMethodTotal(string Code, string Name, string? NameSi, string? NameTa, int Gifts, decimal Amount);

/// <summary>Gifts of items in one need category; voided gifts are not in it.</summary>
/// <param name="Code">The category, or <see langword="null"/> for gifts that came for no need.</param>
/// <param name="EstimatedValue">What staff estimated, for the gifts they estimated; <see langword="null"/> for none.</param>
public sealed record ReportCategoryTotal(string? Code, string? Name, string? NameSi, string? NameTa, int Gifts, decimal? EstimatedValue);

/// <summary>A voided gift, listed apart from the totals.</summary>
public sealed record ReportVoidedGift(
    Guid PublicId,
    string ReceiptNo,
    DateTime DonatedAt,
    string DonorName,
    string Kind,
    decimal? Amount,
    string? Items,
    decimal? Quantity,
    string VoidReason,
    DateTime VoidedAt,
    string VoidedByName);

/// <summary>Needs in a month.</summary>
/// <param name="Opened">Put on the board in the month.</param>
/// <param name="Fulfilled">Fulfilled in the month.</param>
/// <param name="StillOpen">Open as the month ended, or now for a month not over.</param>
public sealed record ReportNeedCounts(int Opened, int Fulfilled, int StillOpen);

/// <summary>What the database holds of a month, before served days are judged.</summary>
/// <param name="BookedDays">The days each slot had an approved or completed booking.</param>
public sealed record MonthlyReportData(
    string Slug,
    string Name,
    string? NameSi,
    string? NameTa,
    IReadOnlyList<ReportStatusCount> Bookings,
    IReadOnlyList<ReportSlot> Slots,
    IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>> BookedDays,
    IReadOnlyList<ReportMethodTotal> Money,
    IReadOnlyList<ReportCategoryTotal> InKind,
    IReadOnlyList<ReportVoidedGift> Voided,
    ReportNeedCounts Needs);

/// <summary>The giving line on the institution's Day Ledger (M18).</summary>
/// <param name="Money">Money received this Colombo month, without voided gifts.</param>
/// <param name="OpenNeeds">Needs open now.</param>
/// <param name="PledgesDue">Open pledges expected within the week, or already overdue.</param>
public sealed record GivingSummaryView(decimal Money, int OpenNeeds, int PledgesDue);

/// <summary>Dapper reads for the institution's reports (plan 09, M16, M18).</summary>
/// <remarks>
/// Row-Level Security keeps every row to the caller's institution; each query names it as well, so a
/// platform connection still reads only that institution.
/// </remarks>
public interface IReportQueries
{
    /// <summary>The month starting <paramref name="first"/>, or <see langword="null"/> if there is no such institution.</summary>
    Task<MonthlyReportData?> GetMonthlyAsync(int institutionId, DateOnly first, CancellationToken cancellationToken = default);

    /// <summary>The figures of the institution's giving line on <paramref name="today"/>, a Colombo date.</summary>
    Task<GivingSummaryView> GetGivingSummaryAsync(int institutionId, DateOnly today, CancellationToken cancellationToken = default);
}

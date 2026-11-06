using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Discovery;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Giving.Reports;

/// <summary>A meal slot's use of a month.</summary>
/// <param name="BookedDays">Served days with at least one approved or completed booking.</param>
/// <param name="ServedDays">Days the slot served; <see langword="null"/> when they can no longer be judged (M37).</param>
/// <param name="Percent">Whole percent of served days booked; <see langword="null"/> with no served days.</param>
public sealed record SlotUtilisationView(Guid MealSlot, string Name, string? NameSi, string? NameTa, int BookedDays, int? ServedDays, int? Percent);

/// <summary>An institution's month (plan 09, M16).</summary>
/// <param name="Month"><c>yyyy-MM</c>.</param>
/// <param name="MonthOver">Whether the month has ended; if not, utilisation counts up to today (M37).</param>
/// <param name="Bookings">Every booking status, in the order a booking moves through them.</param>
/// <param name="MoneyTotal">All the money in <paramref name="Money"/>.</param>
public sealed record MonthlyReportView(
    string Month,
    bool MonthOver,
    string Name,
    string? NameSi,
    string? NameTa,
    IReadOnlyList<ReportStatusCount> Bookings,
    IReadOnlyList<SlotUtilisationView> Utilisation,
    IReadOnlyList<ReportMethodTotal> Money,
    decimal MoneyTotal,
    IReadOnlyList<ReportCategoryTotal> InKind,
    IReadOnlyList<ReportVoidedGift> Voided,
    ReportNeedCounts Needs);

/// <summary>
/// The monthly report (plan 09, M16): the database's counts, with utilisation worked out here because
/// which days a slot served is the booking rules' answer, not SQL's.
/// </summary>
/// <remarks>
/// Served days are what <see cref="AvailabilityCalendar"/> says of each day, the judge behind both its
/// month and <see cref="AvailabilityCalendar.DaysAsync"/>, read through
/// <see cref="MealSlotRules.IsServedDay"/>. The calendar knows only an active institution's active slots,
/// and judges by the slot's rules as they stand now: there is no history of them (M37).
/// </remarks>
public sealed class MonthlyReportHandler(IReportQueries queries, AvailabilityCalendar calendar, TimeProvider timeProvider)
{
    private static readonly string[] StatusOrder =
    [
        BookingStatus.Requested, BookingStatus.Approved, BookingStatus.Completed,
        BookingStatus.NoShow, BookingStatus.Rejected, BookingStatus.Cancelled,
    ];

    /// <summary>The month starting <paramref name="first"/>, or <see langword="null"/> if there is no such institution.</summary>
    public async Task<MonthlyReportView?> GetAsync(int institutionId, DateOnly first, CancellationToken cancellationToken = default)
    {
        var data = await queries.GetMonthlyAsync(institutionId, first, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return null;
        }

        var month = await calendar.GetAsync(data.Slug, first, cancellationToken).ConfigureAwait(false);
        var judged = month?.Slots.ToDictionary(
            slot => slot.MealSlot,
            slot => slot.Days.Where(day => MealSlotRules.IsServedDay(day.Reason)).Select(day => day.Date).ToList());

        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        var last = first.AddMonths(1).AddDays(-1);
        var counted = last < today ? last : today;

        var bookings = StatusOrder
            .Select(status => new ReportStatusCount(status, data.Bookings.FirstOrDefault(b => b.Status == status)?.Count ?? 0))
            .ToList();

        return new MonthlyReportView(
            first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            last < today,
            data.Name,
            data.NameSi,
            data.NameTa,
            bookings,
            [.. data.Slots.Select(slot => Utilisation(slot, data.BookedDays.GetValueOrDefault(slot.MealSlot), judged?.GetValueOrDefault(slot.MealSlot), counted))],
            data.Money,
            data.Money.Sum(m => m.Amount),
            data.InKind,
            data.Voided,
            data.Needs);
    }

    /// <param name="judged">The slot's served days, or <see langword="null"/> if the calendar no longer knows it.</param>
    /// <param name="counted">The last day counted: the month's, or today in a month not over.</param>
    private static SlotUtilisationView Utilisation(ReportSlot slot, IReadOnlySet<DateOnly>? booked, List<DateOnly>? judged, DateOnly counted)
    {
        booked ??= new HashSet<DateOnly>();

        if (judged is null)
        {
            return new SlotUtilisationView(slot.MealSlot, slot.Name, slot.NameSi, slot.NameTa, booked.Count(day => day <= counted), null, null);
        }

        var firstDay = SriLankaTime.Today(slot.CreatedAt);
        var served = judged.Where(day => day >= firstDay && day <= counted).ToList();

        // A booked day that is not a served day (blocked afterwards) is not counted: never above 100%.
        var bookedDays = served.Count(booked.Contains);
        int? percent = served.Count == 0
            ? null
            : (int)Math.Round(bookedDays * 100m / served.Count, MidpointRounding.AwayFromZero);

        return new SlotUtilisationView(slot.MealSlot, slot.Name, slot.NameSi, slot.NameTa, bookedDays, served.Count, percent);
    }
}

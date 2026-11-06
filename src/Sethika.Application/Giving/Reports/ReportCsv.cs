using System.Globalization;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Giving.Reports;

/// <summary>
/// The report and the donations list as files (plan 09, M17, M27). Names the institution keeps in three
/// languages come in the caller's; headings are English, and statuses and kinds are the stored words.
/// </summary>
public static class ReportCsv
{
    /// <summary>The month's report: each part under its own heading, a blank line between.</summary>
    /// <param name="language"><c>si</c>, <c>ta</c>, or anything else for English.</param>
    public static byte[] Monthly(MonthlyReportView report, string? language)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new CsvDocument()
            .Row("Monthly report", InLanguage(language, report.Name, report.NameSi, report.NameTa), report.Month);

        if (!report.MonthOver)
        {
            csv.Row("The month is not over: utilisation counts up to today.");
        }

        csv.Gap().Row("Bookings by status").Row("Status", "Bookings");
        foreach (var row in report.Bookings)
        {
            csv.Row(row.Status, Count(row.Count));
        }

        csv.Gap().Row("Utilisation per meal slot").Row("Meal slot", "Days booked", "Days served", "Percent");
        foreach (var row in report.Utilisation)
        {
            csv.Row(InLanguage(language, row.Name, row.NameSi, row.NameTa), Count(row.BookedDays), Count(row.ServedDays), Count(row.Percent));
        }

        csv.Gap().Row("Money received by method").Row("Method", "Gifts", "Amount (LKR)");
        foreach (var row in report.Money)
        {
            csv.Row(InLanguage(language, row.Name, row.NameSi, row.NameTa), Count(row.Gifts), CsvDocument.Money(row.Amount));
        }

        csv.Row("Total", null, CsvDocument.Money(report.MoneyTotal));

        csv.Gap().Row("Gifts of items by category").Row("Category", "Gifts", "Estimated value (LKR)");
        foreach (var row in report.InKind)
        {
            var category = row.Name is null ? "Not for a need" : InLanguage(language, row.Name, row.NameSi, row.NameTa);
            csv.Row(category, Count(row.Gifts), row.EstimatedValue is { } value ? CsvDocument.Money(value) : null);
        }

        csv.Gap().Row("Voided gifts (not in any total)").Row("Receipt", "Date", "Donor", "Kind", "Amount (LKR)", "Items", "Quantity", "Reason", "Voided by", "Voided on");
        foreach (var row in report.Voided)
        {
            csv.Row(
                row.ReceiptNo, Day(row.DonatedAt), row.DonorName, row.Kind, row.Amount is { } amount ? CsvDocument.Money(amount) : null,
                row.Items, row.Quantity is { } quantity ? CsvDocument.Number(quantity) : null, row.VoidReason, row.VoidedByName, Day(row.VoidedAt));
        }

        csv.Gap().Row("Needs").Row("Opened", "Fulfilled", "Still open").Row(Count(report.Needs.Opened), Count(report.Needs.Fulfilled), Count(report.Needs.StillOpen));
        return csv.ToBytes();
    }

    /// <summary>A month's gifts as the list shows them, voided ones marked (M6).</summary>
    /// <param name="methods">The payment methods' names.</param>
    /// <param name="language"><c>si</c>, <c>ta</c>, or anything else for English.</param>
    public static byte[] Donations(IReadOnlyList<DonationListItem> rows, IReadOnlyList<LookupItem> methods, string? language)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(methods);

        var csv = new CsvDocument().Row("Receipt", "Date", "Donor", "Kind", "Amount (LKR)", "Method", "Items", "Quantity", "Recorded by", "Voided", "Voided on");

        foreach (var row in rows)
        {
            csv.Row(
                row.ReceiptNo, Day(row.DonatedAt), row.DonorName, row.Kind, row.Amount is { } amount ? CsvDocument.Money(amount) : null,
                MethodName(row.Method, methods, language), row.Items, row.Quantity is { } quantity ? CsvDocument.Number(quantity) : null,
                row.RecordedByName, row.IsVoided ? "Yes" : "No", row.VoidedAt is { } voided ? Day(voided) : null);
        }

        return csv.ToBytes();
    }

    private static string? MethodName(string? code, IReadOnlyList<LookupItem> methods, string? language) =>
        methods.FirstOrDefault(m => m.Code == code) is { } method ? InLanguage(language, method.Name, method.NameSi, method.NameTa) : code;

    private static string InLanguage(string? language, string name, string? nameSi, string? nameTa) =>
        language switch
        {
            "si" => nameSi ?? name,
            "ta" => nameTa ?? name,
            _ => name,
        };

    private static string? Count(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Day(DateTime utc) => SriLankaTime.Today(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

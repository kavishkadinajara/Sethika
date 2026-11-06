using System.Text;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Reports;

namespace Sethika.Application.Tests.Giving;

/// <summary>Plan 09, M17, M27: what goes in the two files, in which language, on which Colombo day.</summary>
public class ReportCsvTests
{
    /// <summary>20:00 UTC is already 01:30 the next day in Colombo.</summary>
    private static readonly DateTime LateEvening = new(2030, 3, 9, 20, 0, 0, DateTimeKind.Utc);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    private static MonthlyReportView Report(bool monthOver = true) =>
        new(
            "2030-03",
            monthOver,
            "Alpha Temple",
            "ඇල්ෆා",
            "அல்பா",
            [new ReportStatusCount("Approved", 2)],
            [
                new SlotUtilisationView(Guid.NewGuid(), "Lunch", "දිවා", "மதியம்", 3, 20, 15),
                new SlotUtilisationView(Guid.NewGuid(), "Old slot", null, null, 1, null, null),
            ],
            [new ReportMethodTotal("CASH", "Cash", "මුදල්", "ரொக்கம்", 2, 1250.5m)],
            1250.5m,
            [
                new ReportCategoryTotal("STATIONERY", "Stationery", "ලිපි ද්‍රව්‍ය", null, 2, 500m),
                new ReportCategoryTotal(null, null, null, null, 1, null),
            ],
            [new ReportVoidedGift(Guid.NewGuid(), "RCP-2030-000009", LateEvening, "Sunil", "InKind", null, "Books", 2.5m, "Twice", LateEvening, "Nimal")],
            new ReportNeedCounts(4, 2, 3));

    [Fact]
    public void TheReport_SaysWhoseMonthItIs_InTheCallersLanguage()
    {
        Assert.StartsWith("Monthly report,Alpha Temple,2030-03\r\n", Text(ReportCsv.Monthly(Report(), "en")), StringComparison.Ordinal);
        Assert.StartsWith("Monthly report,ඇල්ෆා,2030-03\r\n", Text(ReportCsv.Monthly(Report(), "si")), StringComparison.Ordinal);
        Assert.StartsWith("Monthly report,அல்பா,2030-03\r\n", Text(ReportCsv.Monthly(Report(), "ta")), StringComparison.Ordinal);
    }

    [Fact]
    public void EachPartOfTheReport_IsInTheFile()
    {
        var text = Text(ReportCsv.Monthly(Report(), "en"));

        Assert.Contains("Approved,2\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Lunch,3,20,15\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Cash,2,1250.50\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Total,,1250.50\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Stationery,2,500.00\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Opened,Fulfilled,Still open\r\n4,2,3\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlotWhoseDaysCannotBeJudged_LeavesThemEmpty_NotZero() =>
        Assert.Contains("Old slot,1,,\r\n", Text(ReportCsv.Monthly(Report(), "en")), StringComparison.Ordinal);

    [Fact]
    public void AGiftForNoNeed_IsNamedInWords_WithNoValue() =>
        Assert.Contains("Not for a need,1,\r\n", Text(ReportCsv.Monthly(Report(), "si")), StringComparison.Ordinal);

    [Fact]
    public void ANameWithNoTranslation_FallsBackToEnglish() =>
        Assert.Contains("Stationery,2,500.00\r\n", Text(ReportCsv.Monthly(Report(), "ta")), StringComparison.Ordinal);

    [Fact]
    public void AVoidedGift_IsDatedInColombo() =>
        Assert.Contains(
            "RCP-2030-000009,2030-03-10,Sunil,InKind,,Books,2.5,Twice,Nimal,2030-03-10\r\n",
            Text(ReportCsv.Monthly(Report(), "en")),
            StringComparison.Ordinal);

    [Fact]
    public void OnlyAMonthNotOver_SaysSo()
    {
        const string Note = "The month is not over";

        Assert.Contains(Note, Text(ReportCsv.Monthly(Report(monthOver: false), "en")), StringComparison.Ordinal);
        Assert.DoesNotContain(Note, Text(ReportCsv.Monthly(Report(monthOver: true), "en")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDonationsFile_DatesInColombo_NamesTheMethod_AndMarksVoidedGifts()
    {
        DonationListItem[] rows =
        [
            new(Guid.NewGuid(), "R1", "Money", 100m, "CASH", null, null, LateEvening, "Sunil", false, "Nimal", false, true, LateEvening, []),
            new(Guid.NewGuid(), "R2", "InKind", null, null, "Books", 2.5m, LateEvening, "Sita", false, "Nimal", false, false, null, []),
            new(Guid.NewGuid(), "R3", "Money", 5m, "RETIRED", null, null, LateEvening, "Kamal", false, "Nimal", false, false, null, []),
        ];
        LookupItem[] methods = [new("CASH", "Cash", "මුදල්", "ரொக்கம்")];

        var lines = Text(ReportCsv.Donations(rows, methods, "si")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Receipt,Date,Donor,Kind,Amount (LKR),Method,Items,Quantity,Recorded by,Voided,Voided on", lines[0]);
        Assert.Equal("R1,2030-03-10,Sunil,Money,100.00,මුදල්,,,Nimal,Yes,2030-03-10", lines[1]);
        Assert.Equal("R2,2030-03-10,Sita,InKind,,,Books,2.5,Nimal,No,", lines[2]);
        Assert.Equal("R3,2030-03-10,Kamal,Money,5.00,RETIRED,,,Nimal,No,", lines[3]);
    }
}

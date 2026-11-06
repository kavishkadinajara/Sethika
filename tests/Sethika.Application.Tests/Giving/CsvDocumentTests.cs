using System.Text;
using Sethika.Application.Giving.Reports;

namespace Sethika.Application.Tests.Giving;

/// <summary>Plan 09, M17: the one CSV writer, its mark, its quoting, and what it refuses to let Excel run.</summary>
public class CsvDocumentTests
{
    private static string Text(CsvDocument csv)
    {
        var bytes = csv.ToBytes();
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public void TheFileStartsWithTheByteOrderMark_AndKeepsSinhalaAndTamil()
    {
        var bytes = new CsvDocument().Row("සුනිල්", "முருகன்").ToBytes();

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("සුනිල්,முருகன்\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("two\rlines", "\"two\rlines\"")]
    public void ACellIsQuotedOnlyWhenItMustBe(string value, string written) =>
        Assert.Equal(written + "\r\n", Text(new CsvDocument().Row(value)));

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+94771234567", "'+94771234567")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("=A1,B1", "\"'=A1,B1\"")]
    [InlineData("a=b", "a=b")]
    public void ACellASpreadsheetWouldRun_IsMadeText(string value, string written) =>
        Assert.Equal(written + "\r\n", Text(new CsvDocument().Row(value)));

    [Fact]
    public void AReturnFirst_IsMadeTextAndQuoted() =>
        Assert.Equal("\"'\rx\"\r\n", Text(new CsvDocument().Row("\rx")));

    [Fact]
    public void AnEmptyCellStaysEmpty_AndAGapIsABlankLine() =>
        Assert.Equal("a,,\r\n\r\nb\r\n", Text(new CsvDocument().Row("a", null, "").Gap().Row("b")));

    [Theory]
    [InlineData("5000", "5000.00")]
    [InlineData("1234.5", "1234.50")]
    public void MoneyHasTwoDecimalsAndNoGrouping(string amount, string written) =>
        Assert.Equal(written, CsvDocument.Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("5", "5")]
    [InlineData("2.50", "2.5")]
    [InlineData("0.25", "0.25")]
    public void ACountHasNoTrailingZeros(string value, string written) =>
        Assert.Equal(written, CsvDocument.Number(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
}

using System.Globalization;
using System.Text;

namespace Sethika.Application.Giving.Reports;

/// <summary>
/// The one CSV writer (plan 09, M17, M27): UTF-8 with a byte order mark, or Excel shows Sinhala and Tamil
/// as noise, and lines ended the way RFC 4180 ends them.
/// </summary>
/// <remarks>
/// A cell a spreadsheet would run as a formula (<c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, or a tab or return
/// before them) gets a leading apostrophe. Donors' names and items are typed by people, some of them over
/// the web, and this file is opened by a committee.
/// </remarks>
public sealed class CsvDocument
{
    private static readonly UTF8Encoding Utf8WithMark = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly char[] NeedsQuotes = [',', '"', '\r', '\n'];

    private readonly StringBuilder _text = new();

    /// <summary>Adds a line of <paramref name="cells"/>; a <see langword="null"/> cell is empty.</summary>
    public CsvDocument Row(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        _text.AppendJoin(',', cells.Select(Cell)).Append("\r\n");
        return this;
    }

    /// <summary>Adds an empty line between two parts of a report.</summary>
    public CsvDocument Gap()
    {
        _text.Append("\r\n");
        return this;
    }

    /// <summary>The file's bytes, the mark first.</summary>
    public byte[] ToBytes() => [.. Utf8WithMark.GetPreamble(), .. Utf8WithMark.GetBytes(_text.ToString())];

    /// <summary>A sum of money, <c>5000.00</c>: no grouping, so a spreadsheet reads it as a number.</summary>
    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A count, whole numbers as they are.</summary>
    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny(NeedsQuotes) >= 0
            ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : safe;
    }
}

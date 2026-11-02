namespace Sethika.Domain.Bookings;

/// <summary>
/// The patterns of a <see cref="StandingSponsorship"/> (plan 08, K2), and the masks their days are
/// stored as. The values are <c>booking.StandingSponsorships.Pattern</c>'s.
/// </summary>
public static class StandingPattern
{
    /// <summary>One day, <see cref="StandingSponsorship.StartDate"/>.</summary>
    public const string Once = "Once";

    /// <summary>Every day.</summary>
    public const string Daily = "Daily";

    /// <summary>Some weekdays, every week.</summary>
    public const string Weekly = "Weekly";

    /// <summary>Some days of the month, every N months from the start month.</summary>
    public const string Monthly = "Monthly";

    /// <summary>One day of one month, every year.</summary>
    public const string Yearly = "Yearly";

    public static readonly IReadOnlyList<string> All = [Once, Daily, Weekly, Monthly, Yearly];

    /// <summary>
    /// The week mask of <paramref name="days"/> (0 Sunday ... 6 Saturday), or <see langword="null"/>
    /// when there are none or one is not a weekday.
    /// </summary>
    public static byte? WeekMask(IEnumerable<int>? days) => (byte?)Mask(days, 6);

    /// <summary>
    /// The month mask of <paramref name="days"/> (1 ... 31), or <see langword="null"/> when there are
    /// none or one is not a day of a month.
    /// </summary>
    public static int? MonthMask(IEnumerable<int>? days) => Mask(days?.Select(d => d - 1), 30);

    /// <summary>The weekdays of a week mask, 0 Sunday ... 6 Saturday.</summary>
    public static IReadOnlyList<int> WeekDaysOf(byte? mask) => Bits(mask ?? 0, 6);

    /// <summary>The days of a month mask, 1 ... 31.</summary>
    public static IReadOnlyList<int> MonthDaysOf(int? mask) => [.. Bits(mask ?? 0, 30).Select(b => b + 1)];

    /// <summary>
    /// Whether <paramref name="month"/>/<paramref name="day"/> is a day some year has: 29 February yes
    /// (it is skipped in common years, Q3), 31 April never. As <c>CK_Standing_YearDate</c>.
    /// </summary>
    public static bool IsDayOfSomeYear(int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2000, month);

    private static int? Mask(IEnumerable<int>? bits, int highest)
    {
        if (bits is null)
        {
            return null;
        }

        var mask = 0;
        foreach (var bit in bits)
        {
            if (bit < 0 || bit > highest)
            {
                return null;
            }

            mask |= 1 << bit;
        }

        return mask == 0 ? null : mask;
    }

    private static List<int> Bits(int mask, int highest) =>
        [.. Enumerable.Range(0, highest + 1).Where(bit => ((mask >> bit) & 1) == 1)];
}

namespace Sethika.Domain.Bookings;

/// <summary>
/// Sri Lanka's wall clock. Every stored time is UTC; a slot's date, its start time and "the day
/// before" are what a donor in Colombo sees on a calendar, so the booking rules count in these.
/// </summary>
/// <remarks>
/// Between 00:00 and 05:30 Colombo time the UTC date is still yesterday's. A rule that took the UTC
/// date would let a donor book a slot that, for them, started hours ago.
/// </remarks>
public static class SriLankaTime
{
    /// <summary>The IANA zone; .NET maps it to "Sri Lanka Standard Time" on Windows.</summary>
    public const string ZoneId = "Asia/Colombo";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    /// <summary>The Colombo wall-clock time at the UTC instant <paramref name="utcNow"/>.</summary>
    public static DateTime FromUtc(DateTime utcNow)
    {
        if (utcNow.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Expected a UTC time, not a local one.", nameof(utcNow));
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Zone);
    }

    /// <summary>Today's date in Colombo at the UTC instant <paramref name="utcNow"/>.</summary>
    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(FromUtc(utcNow));

    /// <summary>
    /// The UTC instant of <paramref name="time"/> on <paramref name="date"/> in Colombo: what a donor
    /// picks on a calendar, as it is stored.
    /// </summary>
    /// <remarks>
    /// Before 05:30 in Colombo the UTC date is the day before, so the date is not simply carried over.
    /// </remarks>
    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), Zone);
}

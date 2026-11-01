using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Colombo's wall clock against stored UTC, both ways.</summary>
public class SriLankaTimeTests
{
    [Theory]
    [InlineData(10, 0, 2026, 11, 20, 4, 30)]  // the morning: same date, 05:30 back
    [InlineData(5, 30, 2026, 11, 20, 0, 0)]   // the first moment that is the same date in UTC
    [InlineData(5, 15, 2026, 11, 19, 23, 45)] // before 05:30 it is still the day before in UTC
    [InlineData(0, 0, 2026, 11, 19, 18, 30)]  // midnight in Colombo
    [InlineData(23, 45, 2026, 11, 20, 18, 15)]
    public void AColomboDateAndTimeIsStoredAsTheUtcInstant(int hour, int minute, int year, int month, int day, int utcHour, int utcMinute)
    {
        var utc = SriLankaTime.ToUtc(new DateOnly(2026, 11, 20), new TimeOnly(hour, minute));

        Assert.Equal(new DateTime(year, month, day, utcHour, utcMinute, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 15)]
    [InlineData(5, 30)]
    [InlineData(23, 59)]
    public void TheUtcInstantReadsBackAsTheSameColomboDateAndTime(int hour, int minute)
    {
        var date = new DateOnly(2026, 12, 31);
        var time = new TimeOnly(hour, minute);

        var utc = SriLankaTime.ToUtc(date, time);

        Assert.Equal(date.ToDateTime(time), SriLankaTime.FromUtc(utc));
        Assert.Equal(date, SriLankaTime.Today(utc));
    }
}

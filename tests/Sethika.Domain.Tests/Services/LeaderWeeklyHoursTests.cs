using Sethika.Domain.Services;

namespace Sethika.Domain.Tests.Services;

/// <summary>Phase E, promise 3: whether a service falls inside a leader's usual hours (the picker's filter).</summary>
public class LeaderWeeklyHoursTests
{
    /// <summary>Fridays, 08:00 to 12:00 Colombo time.</summary>
    private static readonly LeaderWeeklyHours FridayMornings = new()
    {
        DayOfWeek = (byte)DayOfWeek.Friday,
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(12, 0),
    };

    private static DateTime Friday(int hour, int minute = 0) => new(2026, 11, 20, hour, minute, 0);

    [Fact]
    public void AServiceInsideTheHoursIsCovered()
    {
        Assert.True(FridayMornings.Covers(Friday(9), Friday(11)));
    }

    [Fact]
    public void TheHoursIncludeTheirStartAndTheirEnd()
    {
        Assert.True(FridayMornings.Covers(Friday(8), Friday(12)));
    }

    [Fact]
    public void AServiceThatRunsPastTheEndOrStartsTooEarlyIsNotCovered()
    {
        Assert.False(FridayMornings.Covers(Friday(11), Friday(12, 15)));
        Assert.False(FridayMornings.Covers(Friday(7, 45), Friday(9)));
    }

    [Fact]
    public void OnlyTheSameDayOfTheWeekCounts()
    {
        Assert.False(FridayMornings.Covers(Friday(9).AddDays(1), Friday(11).AddDays(1)));
    }

    [Fact]
    public void AServiceThatRunsIntoTheNextDayIsNotCovered()
    {
        var allDay = new LeaderWeeklyHours { DayOfWeek = (byte)DayOfWeek.Friday, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59) };

        Assert.False(allDay.Covers(Friday(23), Friday(23).AddHours(2)));
    }
}

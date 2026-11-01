using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 06, G2: the time of day comes from the slot's start time.</summary>
public class MealTimesTests
{
    [Theory]
    [InlineData("00:00", MealTimes.Morning)]
    [InlineData("06:30", MealTimes.Morning)]
    [InlineData("10:29", MealTimes.Morning)]
    [InlineData("10:30", MealTimes.Midday)]
    [InlineData("11:00", MealTimes.Midday)]
    [InlineData("14:59", MealTimes.Midday)]
    [InlineData("15:00", MealTimes.Evening)]
    [InlineData("18:30", MealTimes.Evening)]
    [InlineData("23:59", MealTimes.Evening)]
    public void TheStartTimeSaysWhichMealItIs(string start, string expected) =>
        Assert.Equal(expected, MealTimes.Of(TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void AnyTakesEverySlot_AndEachTimeOfDayOnlyItsOwn()
    {
        var breakfast = new TimeOnly(7, 0);
        var lunch = new TimeOnly(11, 0);
        var dinner = new TimeOnly(18, 0);

        Assert.All([breakfast, lunch, dinner], start => Assert.True(MealTimes.Matches(MealTimes.Any, start)));

        Assert.Equal([true, false, false], [MealTimes.Matches(MealTimes.Morning, breakfast), MealTimes.Matches(MealTimes.Morning, lunch), MealTimes.Matches(MealTimes.Morning, dinner)]);
        Assert.Equal([false, true, false], [MealTimes.Matches(MealTimes.Midday, breakfast), MealTimes.Matches(MealTimes.Midday, lunch), MealTimes.Matches(MealTimes.Midday, dinner)]);
        Assert.Equal([false, false, true], [MealTimes.Matches(MealTimes.Evening, breakfast), MealTimes.Matches(MealTimes.Evening, lunch), MealTimes.Matches(MealTimes.Evening, dinner)]);
    }

    [Fact]
    public void AnUnknownTimeOfDayMatchesNothing() =>
        Assert.False(MealTimes.Matches("brunch", new TimeOnly(11, 0)));
}

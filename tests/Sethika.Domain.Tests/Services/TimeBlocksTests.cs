using Sethika.Domain.Services;

namespace Sethika.Domain.Tests.Services;

/// <summary>Phase E, V2 and V5: the 15-minute blocks an assignment or leave writes.</summary>
public class TimeBlocksTests
{
    private static DateTime Utc(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void AServiceIsCoveredFromItsStartUpToButNotIncludingItsEnd()
    {
        var blocks = TimeBlocks.For(Utc(20, 3, 30), Utc(20, 4, 30));

        Assert.Equal([Utc(20, 3, 30), Utc(20, 3, 45), Utc(20, 4, 0), Utc(20, 4, 15)], blocks);
    }

    [Fact]
    public void TwoServicesThatTouchShareNoBlock()
    {
        var morning = TimeBlocks.For(Utc(20, 3, 0), Utc(20, 4, 0));
        var next = TimeBlocks.For(Utc(20, 4, 0), Utc(20, 5, 0));

        Assert.Empty(morning.Intersect(next));
    }

    [Fact]
    public void ServicesThatOverlapByOneQuarterShareExactlyThatBlock()
    {
        var first = TimeBlocks.For(Utc(20, 3, 0), Utc(20, 4, 15));
        var second = TimeBlocks.For(Utc(20, 4, 0), Utc(20, 5, 0));

        Assert.Equal([Utc(20, 4, 0)], first.Intersect(second));
    }

    [Fact]
    public void ABufferIsAddedOnBothSides()
    {
        var blocks = TimeBlocks.For(Utc(20, 4, 0), Utc(20, 5, 0), bufferMinutes: 30);

        Assert.Equal(Utc(20, 3, 30), blocks[0]);
        Assert.Equal(Utc(20, 5, 15), blocks[^1]);
        Assert.Equal(8, blocks.Count);
    }

    [Fact]
    public void BackToBackHomeVisitsClashBecauseOfTheirTravelTime()
    {
        var first = TimeBlocks.For(Utc(20, 3, 0), Utc(20, 4, 0), bufferMinutes: 30);
        var second = TimeBlocks.For(Utc(20, 4, 0), Utc(20, 5, 0), bufferMinutes: 30);

        Assert.NotEmpty(first.Intersect(second));
    }

    [Fact]
    public void AnIntervalThatCrossesMidnightContinuesIntoTheNextDay()
    {
        // 23:30 to 01:00 UTC.
        var blocks = TimeBlocks.For(Utc(20, 23, 30), Utc(21, 1, 0));

        Assert.Equal(6, blocks.Count);
        Assert.Equal(Utc(21, 0, 0), blocks[2]);
        Assert.Equal(Utc(21, 0, 45), blocks[^1]);
    }

    [Fact]
    public void AWeekOfLeaveIs672Blocks()
    {
        Assert.Equal(672, TimeBlocks.For(Utc(20, 0, 0), Utc(27, 0, 0)).Count);
    }

    [Fact]
    public void EveryBlockIsUtcEvenFromTimesReadWithoutAKind()
    {
        // SQL Server's DATETIME2 comes back from Dapper as Unspecified.
        var start = DateTime.SpecifyKind(Utc(20, 3, 0), DateTimeKind.Unspecified);

        Assert.All(TimeBlocks.For(start, start.AddHours(1)), b => Assert.Equal(DateTimeKind.Utc, b.Kind));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(45, 0, true)]
    [InlineData(10, 0, false)]
    [InlineData(15, 30, false)]
    public void OnlyQuarterHoursAreOnTheGrid(int minute, int second, bool aligned)
    {
        Assert.Equal(aligned, TimeBlocks.IsAligned(new DateTime(2026, 11, 20, 4, minute, second, DateTimeKind.Utc)));
    }

    [Fact]
    public void AMillisecondOffTheGridIsOffTheGrid()
    {
        Assert.False(TimeBlocks.IsAligned(Utc(20, 4, 0).AddMilliseconds(1)));
    }

    [Fact]
    public void TimesOffTheGridOrBackwardsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => TimeBlocks.For(Utc(20, 4, 10), Utc(20, 5, 0)));
        Assert.Throws<ArgumentException>(() => TimeBlocks.For(Utc(20, 5, 0), Utc(20, 4, 0)));
        Assert.Throws<ArgumentException>(() => TimeBlocks.For(Utc(20, 4, 0), Utc(20, 4, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeBlocks.For(Utc(20, 4, 0), Utc(20, 5, 0), bufferMinutes: 20));
    }
}

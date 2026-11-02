using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>
/// Plan 08, K2 and Q3: which days a standing sponsorship's pattern falls on. Every expected list here
/// is written out by hand from a calendar, never computed, so the arithmetic is checked against days.
/// </summary>
public class StandingSponsorshipTests
{
    private static readonly DateOnly Start = new(2026, 11, 10);

    private static StandingSponsorship Sponsorship(
        string pattern,
        DateOnly? start = null,
        DateOnly? end = null,
        byte interval = 1,
        int[]? weekDays = null,
        int[]? monthDays = null,
        byte? yearMonth = null,
        byte? yearDay = null) =>
        new()
        {
            Pattern = pattern,
            StartDate = start ?? Start,
            EndDate = end,
            MonthInterval = interval,
            WeekDays = StandingPattern.WeekMask(weekDays),
            MonthDays = StandingPattern.MonthMask(monthDays),
            YearMonth = yearMonth,
            YearDay = yearDay,
        };

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DateOnly[] Days(StandingSponsorship sponsorship, DateOnly from, DateOnly to) => [.. sponsorship.DaysBetween(from, to)];

    // ------------------------------------------------------------------ the patterns

    [Fact]
    public void Once_IsItsStartDayOnly()
    {
        var once = Sponsorship(StandingPattern.Once, end: Start);

        Assert.Equal([Start], Days(once, D(2026, 11, 1), D(2027, 12, 31)));
        Assert.False(once.OccursOn(Start.AddDays(1)));
        Assert.False(once.OccursOn(Start.AddYears(1)));
    }

    [Fact]
    public void Once_IsOneDay_ByItsPattern_NotOnlyByItsEnd()
    {
        // The database gives a Once an end on its start (CK_Standing_Shape); the pattern does not lean on it.
        var once = Sponsorship(StandingPattern.Once);

        Assert.Equal([Start], Days(once, D(2026, 11, 1), D(2026, 12, 31)));
    }

    [Fact]
    public void Daily_IsEveryDayFromItsStart()
    {
        var daily = Sponsorship(StandingPattern.Daily);

        Assert.Equal([D(2026, 11, 10), D(2026, 11, 11), D(2026, 11, 12)], Days(daily, D(2026, 11, 1), D(2026, 11, 12)));
    }

    [Fact]
    public void Weekly_IsTheWeekdaysOfItsMask()
    {
        // 10 November 2026 is a Tuesday. Sunday (0) and Wednesday (3).
        var weekly = Sponsorship(StandingPattern.Weekly, weekDays: [0, 3]);

        Assert.Equal(
            [D(2026, 11, 11), D(2026, 11, 15), D(2026, 11, 18), D(2026, 11, 22)],
            Days(weekly, D(2026, 11, 10), D(2026, 11, 24)));
    }

    [Fact]
    public void Weekly_SaturdayIsTheTopBit()
    {
        var saturdays = Sponsorship(StandingPattern.Weekly, weekDays: [6]);

        Assert.Equal([D(2026, 11, 14), D(2026, 11, 21)], Days(saturdays, D(2026, 11, 10), D(2026, 11, 22)));
    }

    [Fact]
    public void Monthly_HoldsSeveralDaysAMonth()
    {
        var monthly = Sponsorship(StandingPattern.Monthly, monthDays: [5, 20]);

        // The 5th of November is before the start.
        Assert.Equal(
            [D(2026, 11, 20), D(2026, 12, 5), D(2026, 12, 20), D(2027, 1, 5), D(2027, 1, 20)],
            Days(monthly, D(2026, 11, 1), D(2027, 1, 31)));
    }

    [Fact]
    public void Monthly_EveryOtherMonth_CountsFromTheStartMonth()
    {
        var everyOther = Sponsorship(StandingPattern.Monthly, interval: 2, monthDays: [15]);

        Assert.Equal(
            [D(2026, 11, 15), D(2027, 1, 15), D(2027, 3, 15), D(2027, 5, 15)],
            Days(everyOther, D(2026, 11, 1), D(2027, 6, 30)));
    }

    [Fact]
    public void Monthly_Quarterly_CountsFromTheStartMonth_AcrossTheYear()
    {
        var quarterly = Sponsorship(StandingPattern.Monthly, start: D(2026, 12, 1), interval: 3, monthDays: [1]);

        Assert.Equal(
            [D(2026, 12, 1), D(2027, 3, 1), D(2027, 6, 1), D(2027, 9, 1), D(2027, 12, 1)],
            Days(quarterly, D(2026, 11, 1), D(2027, 12, 31)));
    }

    [Fact]
    public void Monthly_EveryTwelveMonths_IsOnceAYear()
    {
        var yearlyByMonth = Sponsorship(StandingPattern.Monthly, interval: 12, monthDays: [25]);

        Assert.Equal([D(2026, 11, 25), D(2027, 11, 25)], Days(yearlyByMonth, D(2026, 11, 1), D(2028, 10, 31)));
    }

    [Fact]
    public void Monthly_The31st_IsSkippedInShorterMonths_NotMoved()
    {
        var lastDays = Sponsorship(StandingPattern.Monthly, start: D(2027, 1, 1), monthDays: [31]);

        // No 31st in February, April or June, and nothing on the 28th or 30th in their place (Q3).
        Assert.Equal(
            [D(2027, 1, 31), D(2027, 3, 31), D(2027, 5, 31), D(2027, 7, 31)],
            Days(lastDays, D(2027, 1, 1), D(2027, 7, 31)));
    }

    [Fact]
    public void Monthly_The29th_IsInFebruaryOnlyInALeapYear()
    {
        var the29th = Sponsorship(StandingPattern.Monthly, start: D(2027, 1, 1), monthDays: [29]);

        Assert.DoesNotContain(D(2027, 2, 28), Days(the29th, D(2027, 2, 1), D(2027, 3, 1)));
        Assert.Empty(Days(the29th, D(2027, 2, 1), D(2027, 2, 28)));
        Assert.Equal([D(2028, 2, 29)], Days(the29th, D(2028, 2, 1), D(2028, 2, 29)));
    }

    [Fact]
    public void Yearly_IsItsMonthAndDay()
    {
        var yearly = Sponsorship(StandingPattern.Yearly, yearMonth: 5, yearDay: 12);

        Assert.Equal([D(2027, 5, 12), D(2028, 5, 12)], Days(yearly, D(2026, 11, 1), D(2028, 12, 31)));
    }

    [Fact]
    public void Yearly_29February_IsSkippedInCommonYears()
    {
        var leapDay = Sponsorship(StandingPattern.Yearly, yearMonth: 2, yearDay: 29);

        Assert.Equal([D(2028, 2, 29), D(2032, 2, 29)], Days(leapDay, D(2026, 11, 1), D(2032, 12, 31)));
        Assert.False(leapDay.OccursOn(D(2027, 2, 28)));
        Assert.False(leapDay.OccursOn(D(2027, 3, 1)));
    }

    // ------------------------------------------------------------------ start and end

    [Fact]
    public void NothingFallsBeforeTheStartOrAfterTheEnd()
    {
        var daily = Sponsorship(StandingPattern.Daily, end: D(2026, 11, 12));

        Assert.False(daily.OccursOn(D(2026, 11, 9)));
        Assert.True(daily.OccursOn(D(2026, 11, 10)));
        Assert.True(daily.OccursOn(D(2026, 11, 12)));
        Assert.False(daily.OccursOn(D(2026, 11, 13)));
        Assert.Equal([D(2026, 11, 10), D(2026, 11, 11), D(2026, 11, 12)], Days(daily, D(2026, 11, 1), D(2026, 12, 31)));
    }

    [Fact]
    public void DaysBetween_IncludesBothEnds()
    {
        var daily = Sponsorship(StandingPattern.Daily);

        Assert.Equal([D(2026, 11, 20), D(2026, 11, 21)], Days(daily, D(2026, 11, 20), D(2026, 11, 21)));
    }

    [Fact]
    public void NextDays_TakesTheFirstFew_LookingYearsAhead()
    {
        var leapDay = Sponsorship(StandingPattern.Yearly, yearMonth: 2, yearDay: 29);

        Assert.Equal([D(2028, 2, 29), D(2032, 2, 29)], leapDay.NextDays(D(2026, 11, 10), 2, horizonDays: 366 * 6));
        Assert.Equal([D(2028, 2, 29)], leapDay.NextDays(D(2026, 11, 10), 5, horizonDays: 366 * 4));
    }

    // ------------------------------------------------------------------ ending

    [Fact]
    public void Ending_RecordsWhoAndWhen_Once()
    {
        var sponsorship = Sponsorship(StandingPattern.Daily);
        var by = Guid.CreateVersion7();
        var at = new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc);

        Assert.True(sponsorship.End(by, at));
        Assert.Equal((StandingSponsorship.EndedStatus, (DateTime?)at, (Guid?)by), (sponsorship.Status, sponsorship.EndedAt, sponsorship.EndedBy));
        Assert.False(sponsorship.IsActive);

        Assert.False(sponsorship.End(Guid.CreateVersion7(), at.AddDays(1)));
        Assert.Equal(((DateTime?)at, (Guid?)by), (sponsorship.EndedAt, sponsorship.EndedBy));
    }

    // ------------------------------------------------------------------ the masks

    [Fact]
    public void WeekMask_SundayIsBitZero_AndSaturdayBitSix()
    {
        Assert.Equal((byte)1, StandingPattern.WeekMask([0]));
        Assert.Equal((byte)64, StandingPattern.WeekMask([6]));
        Assert.Equal((byte)127, StandingPattern.WeekMask([0, 1, 2, 3, 4, 5, 6]));
        Assert.Equal([0, 3], StandingPattern.WeekDaysOf(StandingPattern.WeekMask([3, 0, 3])));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 7 })]
    [InlineData(new[] { -1 })]
    public void WeekMask_RefusesNoDaysAndDaysThatAreNotWeekdays(int[] days) => Assert.Null(StandingPattern.WeekMask(days));

    [Fact]
    public void MonthMask_The1stIsBitZero_AndThe31stBitThirty()
    {
        Assert.Equal(1, StandingPattern.MonthMask([1]));
        Assert.Equal(1 << 30, StandingPattern.MonthMask([31]));
        Assert.Equal([5, 20, 31], StandingPattern.MonthDaysOf(StandingPattern.MonthMask([31, 5, 20])));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 32 })]
    [InlineData(new[] { 5, 32 })]
    public void MonthMask_RefusesNoDaysAndDaysNoMonthHas(int[] days) => Assert.Null(StandingPattern.MonthMask(days));

    [Theory]
    [InlineData(2, 29, true)]
    [InlineData(2, 30, false)]
    [InlineData(4, 30, true)]
    [InlineData(4, 31, false)]
    [InlineData(12, 31, true)]
    [InlineData(13, 1, false)]
    [InlineData(1, 0, false)]
    public void ADayOfSomeYear_HasFebruary29_ButNeverApril31(int month, int day, bool expected) =>
        Assert.Equal(expected, StandingPattern.IsDayOfSomeYear(month, day));

    // ------------------------------------------------------------------ a swap from now on (K8)

    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EndingBeforeADay_EndsIt_OnTheDayBefore()
    {
        var weekly = Sponsorship(StandingPattern.Weekly, weekDays: [2]);

        Assert.True(weekly.EndBefore(D(2026, 11, 24), Staff, Now));

        Assert.Equal((StandingSponsorship.EndedStatus, (DateOnly?)D(2026, 11, 23), (Guid?)Staff), (weekly.Status, weekly.EndDate, weekly.EndedBy));
        Assert.False(weekly.OccursOn(D(2026, 11, 24)));
        Assert.True(weekly.OccursOn(D(2026, 11, 17)));
        Assert.False(weekly.EndBefore(D(2026, 12, 1), Staff, Now));
    }

    [Fact]
    public void EndingBeforeItsFirstDay_KeepsItsDates()
    {
        var weekly = Sponsorship(StandingPattern.Weekly, end: D(2027, 1, 31), weekDays: [2]);

        weekly.EndBefore(Start, Staff, Now);

        Assert.Equal((StandingSponsorship.EndedStatus, (DateOnly?)D(2027, 1, 31)), (weekly.Status, weekly.EndDate));
    }

    [Fact]
    public void EndingBeforeADayAfterItsEnd_KeepsItsEnd()
    {
        var weekly = Sponsorship(StandingPattern.Weekly, end: D(2026, 11, 30), weekDays: [2]);

        weekly.EndBefore(D(2026, 12, 15), Staff, Now);

        Assert.Equal(D(2026, 11, 30), weekly.EndDate);
    }

    [Fact]
    public void TakingOver_GivesThisDonorsMeal_OnTheOthersPattern_FromItsDay()
    {
        var mine = Sponsorship(StandingPattern.Weekly, weekDays: [2]);
        (mine.Id, mine.InstitutionId, mine.InstitutionDonorId, mine.MealSlotId) = (7, 3, 11, 5);
        (mine.Headcount, mine.FulfilmentMode, mine.InMemoryOf, mine.Notes) = (25, FulfilmentMode.InstitutionPrepares, "Appachchi", "Rice and curry");

        // Every other month on the 5th from October: October, December, February.
        var theirs = Sponsorship(StandingPattern.Monthly, start: D(2026, 10, 5), end: D(2027, 6, 30), interval: 2, monthDays: [5]);
        theirs.InstitutionDonorId = 12;

        var id = Guid.CreateVersion7();
        var taken = mine.TakingOver(theirs, D(2026, 12, 5), id, Staff, Now);

        Assert.Equal((id, 3, 11, 5, 0), (taken.PublicId, taken.InstitutionId, taken.InstitutionDonorId, taken.MealSlotId, taken.Id));
        Assert.Equal(((short)25, FulfilmentMode.InstitutionPrepares, "Appachchi", "Rice and curry"), (taken.Headcount, taken.FulfilmentMode, taken.InMemoryOf, taken.Notes));
        Assert.Equal((StandingSponsorship.ActiveStatus, Staff, Now), (taken.Status, taken.CreatedBy, taken.CreatedAt));

        // Started on one of their days, it keeps their months: December, February, April, June.
        Assert.Equal([D(2026, 12, 5), D(2027, 2, 5), D(2027, 4, 5), D(2027, 6, 5)], Days(taken, D(2026, 11, 1), D(2027, 12, 31)));
    }
}

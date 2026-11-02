using System.Globalization;
using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 07, H6: the days a death is remembered on, from the date of passing and the rules.</summary>
public class MemorialObservancesTests
{
    private static readonly MemorialRule SeventhDay = Rule(1, "SEVENTH", months: 0, days: 6, sort: 10);
    private static readonly MemorialRule ThreeMonths = Rule(2, "THREE_MONTHS", months: 3, days: 0, sort: 20);
    private static readonly MemorialRule Yearly = Rule(3, "YEARLY", months: 12, days: 0, sort: 30, yearly: true);

    [Theory]
    [InlineData("2026-11-01", 0, 6, "2026-11-07")]
    [InlineData("2026-12-28", 0, 6, "2027-01-03")]
    [InlineData("2026-05-31", 3, 0, "2026-08-31")]
    [InlineData("2026-11-30", 3, 0, "2027-02-28")]
    [InlineData("2027-11-30", 3, 0, "2028-02-29")]
    [InlineData("2026-01-31", 1, 1, "2026-03-01")]
    [InlineData("2026-01-30", 1, 1, "2026-03-01")]
    [InlineData("2026-01-31", 0, 39, "2026-03-11")]
    [InlineData("2028-02-29", 0, 30, "2028-03-30")]
    public void AnObservanceIsTheDateOfPassingPlusItsMonthsThenItsDays(string passing, short months, short days, string expected) =>
        Assert.Equal(Day(expected), MemorialObservances.Date(Day(passing), Rule(9, "R", months, days), 1));

    [Theory]
    [InlineData(1, "2027-11-01")]
    [InlineData(2, "2028-11-01")]
    [InlineData(10, "2036-11-01")]
    public void AYearlyObservanceFallsOnTheAnniversaryEveryYear(int number, string expected) =>
        Assert.Equal(Day(expected), MemorialObservances.Date(Day("2026-11-01"), Yearly, number));

    [Theory]
    [InlineData(1, "2025-02-28")]
    [InlineData(2, "2026-02-28")]
    [InlineData(4, "2028-02-29")]
    [InlineData(8, "2032-02-29")]
    public void ADeathOn29FebruaryIsRememberedOn29FebruaryInALeapYear_AndOn28InTheOthers(int number, string expected) =>
        Assert.Equal(Day(expected), MemorialObservances.Date(Day("2024-02-29"), Yearly, number));

    [Fact]
    public void AnObservanceKeptOnceHasOneDay_WhateverTheNumberAsked() =>
        Assert.Equal(Day("2026-11-07"), MemorialObservances.Date(Day("2026-11-01"), SeventhDay, 3));

    [Theory]
    [InlineData("2026-11-07", 1)]
    [InlineData("2026-11-06", null)]
    [InlineData("2026-11-08", null)]
    [InlineData("2027-11-07", null)]
    public void ADayIsAnObservanceKeptOnceOnlyOnItsDay(string date, int? expected) =>
        Assert.Equal(expected, MemorialObservances.NumberOn(Day("2026-11-01"), SeventhDay, Day(date)));

    [Theory]
    [InlineData("2027-11-01", 1)]
    [InlineData("2030-11-01", 4)]
    [InlineData("2026-11-01", null)]
    [InlineData("2030-11-02", null)]
    [InlineData("2030-10-31", null)]
    [InlineData("9999-11-01", null)]
    public void ADayIsAYearlyObservanceOnEveryAnniversaryFromTheFirst_AndNeverBefore(string date, int? expected) =>
        Assert.Equal(expected, MemorialObservances.NumberOn(Day("2026-11-01"), Yearly, Day(date)));

    [Theory]
    [InlineData("2028-02-29", 4)]
    [InlineData("2027-02-28", 3)]
    [InlineData("2028-02-28", null)]
    public void After29February_ALeapYearsObservanceIsThe29th_AndACommonYearsThe28th(string date, int? expected) =>
        Assert.Equal(expected, MemorialObservances.NumberOn(Day("2024-02-29"), Yearly, Day(date)));

    [Fact]
    public void AYearlyRuleWithDaysToo_FindsEachYearsDay()
    {
        var rule = Rule(4, "LATE_DECEMBER", months: 12, days: 5, yearly: true);

        // 28 December plus 5 days crosses into the next calendar year: still found.
        Assert.Equal(Day("2028-01-02"), MemorialObservances.Date(Day("2026-12-28"), rule, 1));
        Assert.Equal(1, MemorialObservances.NumberOn(Day("2026-12-28"), rule, Day("2028-01-02")));
        Assert.Equal(3, MemorialObservances.NumberOn(Day("2026-12-28"), rule, Day("2030-01-02")));
    }

    [Fact]
    public void AYearlyRuleWhoseDaysCrossTheYearEnd_IsFoundInWhicheverYearALeapDayPutsIt()
    {
        // 351 days from 15 January ends the year in a leap year and crosses it in a common one, so a
        // later year's day can sit a calendar year off from the first's count.
        var rule = Rule(6, "LONG_YEARLY", months: 0, days: 351, yearly: true);

        Assert.Equal(Day("2027-01-01"), MemorialObservances.Date(Day("2026-01-15"), rule, 1));
        Assert.Equal(Day("2028-12-31"), MemorialObservances.Date(Day("2026-01-15"), rule, 3));
        Assert.Equal(3, MemorialObservances.NumberOn(Day("2026-01-15"), rule, Day("2028-12-31")));

        Assert.Equal(Day("2028-12-31"), MemorialObservances.Date(Day("2028-01-15"), rule, 1));
        Assert.Equal(Day("2030-01-01"), MemorialObservances.Date(Day("2028-01-15"), rule, 2));
        Assert.Equal(2, MemorialObservances.NumberOn(Day("2028-01-15"), rule, Day("2030-01-01")));
    }

    [Fact]
    public void APlanShowsEachOnceKeptObservancePastOrNot_AndTheYearlyOnesNextDay_InDateOrder()
    {
        // Passed on 2026-01-10; today is 2026-03-01: the 7th day is past, three months is coming.
        var observances = MemorialObservances.For(Day("2026-01-10"), [Yearly, ThreeMonths, SeventhDay], Day("2026-03-01"));

        Assert.Equal(
            [("SEVENTH", 1, Day("2026-01-16")), ("THREE_MONTHS", 1, Day("2026-04-10")), ("YEARLY", 1, Day("2027-01-10"))],
            observances.Select(o => (o.Rule.Code, o.Number, o.Date)));
    }

    [Theory]
    [InlineData("2031-01-10", 5, "2031-01-10")]
    [InlineData("2031-01-11", 6, "2032-01-10")]
    [InlineData("2031-01-09", 5, "2031-01-10")]
    [InlineData("2026-01-10", 1, "2027-01-10")]
    public void AYearlyObservancesNextDayIsTodayOrLater(string today, int number, string date)
    {
        var next = Assert.Single(MemorialObservances.For(Day("2026-01-10"), [Yearly], Day(today)));
        Assert.Equal((number, Day(date)), (next.Number, next.Date));
    }

    [Fact]
    public void ANewRuleIsANewObservance_WithNoChangeToTheCode()
    {
        var fortiethDay = Rule(5, "FORTIETH", months: 0, days: 39, sort: 15);

        var observances = MemorialObservances.For(Day("2026-01-10"), [SeventhDay, fortiethDay], Day("2026-01-01"));

        Assert.Equal([Day("2026-01-16"), Day("2026-02-18")], observances.Select(o => o.Date));
    }

    [Fact]
    public void AnObservanceIsCountedFromOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MemorialObservances.Date(Day("2026-01-10"), Yearly, 0));

    private static MemorialRule Rule(short id, string code, short months, short days, short sort = 0, bool yearly = false) =>
        new()
        {
            Id = id,
            Code = code,
            Name = code,
            OffsetMonths = months,
            OffsetDays = days,
            RepeatsAnnually = yearly,
            SortOrder = sort,
        };

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

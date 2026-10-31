using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 03, C4: every reason a slot-day cannot be booked, and the order they are checked in.</summary>
public class MealSlotRulesTests
{
    private const int Institution = 7;
    private const int SlotId = 70;

    /// <summary>A Friday. 2026-11-20 06:30 UTC is 12:00 in Colombo.</summary>
    private static readonly DateOnly Friday = new(2026, 11, 20);

    /// <summary>Thursday 2026-11-19, 09:00 in Colombo: the day before <see cref="Friday"/>.</summary>
    private static readonly DateTime ThursdayMorningUtc = new(2026, 11, 19, 3, 30, 0, DateTimeKind.Utc);

    private static MealSlot Slot(Action<MealSlot>? change = null)
    {
        var slot = new MealSlot
        {
            Id = SlotId,
            InstitutionId = Institution,
            Name = "Lunch",
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(12, 30),
            MinHeadcount = 10,
            MaxHeadcount = 60,
            MinAdvanceDays = 1,
            MaxAdvanceDays = 30,
        };
        change?.Invoke(slot);
        return slot;
    }

    private static string? CanBookOn(
        MealSlot slot,
        DateOnly date,
        DateTime? utcNow = null,
        string status = MealSlotRules.ActiveInstitution,
        MealSlotSeason[]? seasons = null,
        BlockedDate[]? blocks = null) =>
        MealSlotRules.CanBookOn(slot, status, seasons ?? [], blocks ?? [], date, utcNow ?? ThursdayMorningUtc);

    private static BlockedDate Block(DateOnly from, DateOnly to, int? slotId = null, int institution = Institution) => new()
    {
        InstitutionId = institution,
        MealSlotId = slotId,
        DateFrom = from,
        DateTo = to,
        Reason = "Festival",
    };

    [Fact]
    public void AnOrdinarySlotDayCanBeBooked()
    {
        Assert.Null(CanBookOn(Slot(), Friday));
        Assert.Null(MealSlotRules.CanBook(Slot(), MealSlotRules.ActiveInstitution, [], [], Friday, ThursdayMorningUtc, 40, FulfilmentMode.DonorBrings));
    }

    [Fact]
    public void AnInactiveSlotCannotBeBooked() =>
        Assert.Equal(BookingErrors.SlotInactive, CanBookOn(Slot(s => s.IsActive = false), Friday));

    [Theory]
    [InlineData("Onboarding")]
    [InlineData("Suspended")]
    [InlineData("Deactivated")]
    public void OnlyAnActiveInstitutionTakesBookings(string status) =>
        Assert.Equal(BookingErrors.InstitutionNotActive, CanBookOn(Slot(), Friday, status: status));

    // ------------------------------------------------------------------ days of the week

    [Fact]
    public void ADayOutsideTheMaskCannotBeBooked()
    {
        var weekends = (byte)(MealSlot.DayBit(DayOfWeek.Saturday) | MealSlot.DayBit(DayOfWeek.Sunday));
        var slot = Slot(s => s.DaysOfWeekMask = weekends);

        Assert.Equal(BookingErrors.NotOnThisDay, CanBookOn(slot, Friday));
        Assert.Null(CanBookOn(slot, Friday.AddDays(1)));
        Assert.Null(CanBookOn(slot, Friday.AddDays(2)));
        Assert.Equal(BookingErrors.NotOnThisDay, CanBookOn(slot, Friday.AddDays(3)));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Monday, 2)]
    [InlineData(DayOfWeek.Saturday, 64)]
    public void TheMaskHasOneBitPerDayStartingWithSunday(DayOfWeek day, byte bit) =>
        Assert.Equal(bit, MealSlot.DayBit(day));

    [Fact]
    public void EachDayOfTheWeekIsItsOwnBit()
    {
        // A slot running only on Fridays is bookable on exactly one day in every seven.
        var slot = Slot(s => { s.DaysOfWeekMask = MealSlot.DayBit(DayOfWeek.Friday); s.MaxAdvanceDays = 365; });

        var bookable = Enumerable.Range(1, 28).Select(Friday.AddDays).Where(d => CanBookOn(slot, d) is null).ToList();

        Assert.Equal([Friday.AddDays(7), Friday.AddDays(14), Friday.AddDays(21), Friday.AddDays(28)], bookable);
    }

    // ------------------------------------------------------------------ seasons and blocks

    [Fact]
    public void ASlotWithSeasonsIsBookableOnlyInsideThem()
    {
        MealSlotSeason[] seasons = [new() { MealSlotId = SlotId, Name = "Season", DateFrom = Friday.AddDays(7), DateTo = Friday.AddDays(9) }];

        Assert.Equal(BookingErrors.OutOfSeason, CanBookOn(Slot(), Friday, seasons: seasons));
        Assert.Null(CanBookOn(Slot(), Friday.AddDays(7), seasons: seasons));
        Assert.Null(CanBookOn(Slot(), Friday.AddDays(9), seasons: seasons));
        Assert.Equal(BookingErrors.OutOfSeason, CanBookOn(Slot(), Friday.AddDays(10), seasons: seasons));
    }

    [Fact]
    public void AnotherSlotsSeasonDoesNotRestrictThisOne()
    {
        MealSlotSeason[] seasons = [new() { MealSlotId = SlotId + 1, Name = "Season", DateFrom = Friday.AddDays(7), DateTo = Friday.AddDays(9) }];

        Assert.Null(CanBookOn(Slot(), Friday, seasons: seasons));
    }

    [Fact]
    public void AnInstitutionWideBlockCoversEverySlotFromFirstDayToLast()
    {
        BlockedDate[] blocks = [Block(Friday, Friday.AddDays(2))];

        Assert.Equal(BookingErrors.DateBlocked, CanBookOn(Slot(), Friday, blocks: blocks));
        Assert.Equal(BookingErrors.DateBlocked, CanBookOn(Slot(), Friday.AddDays(2), blocks: blocks));
        Assert.Null(CanBookOn(Slot(), Friday.AddDays(3), blocks: blocks));
    }

    [Fact]
    public void ABlockOnAnotherSlotOrInstitutionLeavesThisOneOpen()
    {
        BlockedDate[] blocks = [Block(Friday, Friday, slotId: SlotId + 1), Block(Friday, Friday, institution: Institution + 1)];

        Assert.Null(CanBookOn(Slot(), Friday, blocks: blocks));
        Assert.Equal(BookingErrors.DateBlocked, CanBookOn(Slot(), Friday, blocks: [Block(Friday, Friday, slotId: SlotId)]));
    }

    // ------------------------------------------------------------------ the day as planned (today's table)

    [Fact]
    public void ASlotServesOnItsDaysInItsSeasons_WhateverTheAdvanceWindow()
    {
        var fridays = Slot(s => { s.DaysOfWeekMask = MealSlot.DayBit(DayOfWeek.Friday); s.MinAdvanceDays = 5; });
        MealSlotSeason[] seasons = [new() { MealSlotId = SlotId, Name = "Season", DateFrom = Friday, DateTo = Friday.AddDays(7) }];

        // Too soon to book on Friday, and still laid that day.
        Assert.True(MealSlotRules.Serves(fridays, seasons, Friday));
        Assert.False(MealSlotRules.Serves(fridays, seasons, Friday.AddDays(1)));
        Assert.True(MealSlotRules.Serves(fridays, seasons, Friday.AddDays(7)));
        Assert.False(MealSlotRules.Serves(fridays, seasons, Friday.AddDays(14)));
        Assert.True(MealSlotRules.Serves(fridays, [], Friday.AddDays(14)));
        Assert.False(MealSlotRules.Serves(Slot(s => s.IsActive = false), [], Friday));
    }

    // ------------------------------------------------------------------ the advance window, in Colombo dates

    [Fact]
    public void TheAdvanceWindowIsCountedInDaysFromTodayInColombo()
    {
        var slot = Slot(s => { s.MinAdvanceDays = 2; s.MaxAdvanceDays = 5; });

        Assert.Equal(BookingErrors.TooSoon, CanBookOn(slot, Friday));            // 1 day ahead
        Assert.Null(CanBookOn(slot, Friday.AddDays(1)));                        // 2
        Assert.Null(CanBookOn(slot, Friday.AddDays(4)));                        // 5
        Assert.Equal(BookingErrors.TooFar, CanBookOn(slot, Friday.AddDays(5))); // 6
    }

    [Fact]
    public void APastDateIsTooSoon() =>
        Assert.Equal(BookingErrors.TooSoon, CanBookOn(Slot(s => s.MinAdvanceDays = 0), Friday.AddDays(-2)));

    [Fact]
    public void JustAfterMidnightInColomboItIsAlreadyTomorrow()
    {
        // 2026-11-19 19:00 UTC is 00:30 on Friday the 20th in Colombo. The UTC date is still Thursday,
        // so a rule on UTC dates would call Friday "1 day ahead" and accept it.
        var justAfterMidnight = new DateTime(2026, 11, 19, 19, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 11, 20), SriLankaTime.Today(justAfterMidnight));
        Assert.Equal(BookingErrors.TooSoon, CanBookOn(Slot(), Friday, justAfterMidnight));
        Assert.Null(CanBookOn(Slot(), Friday.AddDays(1), justAfterMidnight));
    }

    [Fact]
    public void TheDayOfWeekIsTheColomboDates()
    {
        // The slot-day is a Colombo date. Its first moment, Friday 00:00 in Colombo, is Thursday 18:30
        // in UTC, so a rule that went through a UTC timestamp would see a Thursday and get the mask
        // wrong by a day for every slot.
        var fridaysOnly = Slot(s => s.DaysOfWeekMask = MealSlot.DayBit(DayOfWeek.Friday));
        var nextFriday = Friday.AddDays(7);

        Assert.Equal(DayOfWeek.Thursday, TimeZoneInfo.ConvertTimeToUtc(
            nextFriday.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId)).DayOfWeek);
        Assert.Null(CanBookOn(fridaysOnly, nextFriday));
        Assert.Equal(BookingErrors.NotOnThisDay, CanBookOn(fridaysOnly, nextFriday.AddDays(-1)));
    }

    [Fact]
    public void SameDayBookingClosesWhenTheMealStarts()
    {
        var sameDay = Slot(s => s.MinAdvanceDays = 0);
        var fridayAt1030 = new DateTime(2026, 11, 20, 5, 0, 0, DateTimeKind.Utc);  // 10:30 Colombo
        var fridayAt1100 = new DateTime(2026, 11, 20, 5, 30, 0, DateTimeKind.Utc); // 11:00 Colombo

        Assert.Null(CanBookOn(sameDay, Friday, fridayAt1030));
        Assert.Equal(BookingErrors.TooSoon, CanBookOn(sameDay, Friday, fridayAt1100));
    }

    // ------------------------------------------------------------------ the booking itself

    [Theory]
    [InlineData(9, BookingErrors.HeadcountOutOfRange)]
    [InlineData(10, null)]
    [InlineData(60, null)]
    [InlineData(61, BookingErrors.HeadcountOutOfRange)]
    public void TheHeadcountMustBeInsideTheSlotsRange(short headcount, string? expected) =>
        Assert.Equal(expected, MealSlotRules.CanBook(Slot(), MealSlotRules.ActiveInstitution, [], [], Friday, ThursdayMorningUtc, headcount, FulfilmentMode.DonorBrings));

    [Theory]
    [InlineData(FulfilmentMode.DonorBrings, null)]
    [InlineData(FulfilmentMode.InstitutionPrepares, BookingErrors.FulfilmentNotOffered)]
    [InlineData(FulfilmentMode.Caterer, BookingErrors.FulfilmentNotOffered)]
    [InlineData("donorbrings", BookingErrors.FulfilmentNotOffered)]
    public void OnlyAnOfferedFulfilmentIsAccepted(string fulfilment, string? expected)
    {
        var slot = Slot(s => s.AllowedFulfilment = FulfilmentMode.DonorBrings);

        Assert.Equal(expected, MealSlotRules.CanBook(slot, MealSlotRules.ActiveInstitution, [], [], Friday, ThursdayMorningUtc, 20, fulfilment));
    }

    [Fact]
    public void TheFirstReasonIsTheOneReported()
    {
        // Inactive, on a blocked day, too soon and with a headcount out of range: the slot being off
        // is what the donor needs to know, and it is checked first.
        var slot = Slot(s => s.IsActive = false);

        Assert.Equal(
            BookingErrors.SlotInactive,
            MealSlotRules.CanBook(slot, "Suspended", [], [Block(Friday, Friday)], Friday, Friday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), 1, "Caterer"));
        Assert.Equal(
            BookingErrors.DateBlocked,
            MealSlotRules.CanBook(Slot(), MealSlotRules.ActiveInstitution, [], [Block(Friday, Friday)], Friday, ThursdayMorningUtc, 1, "Caterer"));
    }

    // ------------------------------------------------------------------ the availability calendar (5.5)

    /// <summary>
    /// A slot-day with these counters. The setters are private so no booking code can change a counter
    /// in memory (C2); a test sets them the way Dapper does, by reflection.
    /// </summary>
    private static SlotInstance SlotDay(MealSlot slot, byte bookedSponsors, short bookedHeadcount, string status = SlotInstanceStatus.Open, short? maxHeadcount = null)
    {
        var day = SlotInstance.Open(slot, Friday);
        Set(day, nameof(SlotInstance.BookedSponsors), bookedSponsors);
        Set(day, nameof(SlotInstance.BookedHeadcount), bookedHeadcount);
        Set(day, nameof(SlotInstance.Status), status);
        if (maxHeadcount is { } max)
        {
            Set(day, nameof(SlotInstance.MaxHeadcount), max);
        }

        return day;

        static void Set(SlotInstance day, string property, object value) =>
            typeof(SlotInstance).GetProperty(property)!.SetValue(day, value);
    }

    private static SlotDayOpenings Openings(MealSlot slot, SlotInstance? day, BlockedDate[]? blocks = null) =>
        MealSlotRules.Openings(slot, MealSlotRules.ActiveInstitution, [], blocks ?? [], Friday, ThursdayMorningUtc, day);

    [Fact]
    public void ADayNobodyHasBookedOffersTheWholeSlot()
    {
        var slot = Slot(s => (s.BookingMode, s.MaxSponsors) = (BookingMode.Shared, 4));

        Assert.Equal(new SlotDayOpenings(null, 4, 60), Openings(slot, day: null));
    }

    [Fact]
    public void ADayShowsWhatItsCountersLeave()
    {
        var slot = Slot(s => (s.BookingMode, s.MaxSponsors) = (BookingMode.Shared, 4));

        var openings = Openings(slot, SlotDay(slot, bookedSponsors: 3, bookedHeadcount: 35));

        Assert.True(openings.Bookable);
        Assert.Equal((1, 25), (openings.Sponsors, openings.Headcount));
    }

    [Fact]
    public void ADayWithEverySponsorPlaceTakenIsFull() =>
        Assert.Equal(new SlotDayOpenings(BookingErrors.SlotFull, 0, 50), Openings(Slot(), SlotDay(Slot(), bookedSponsors: 1, bookedHeadcount: 10)));

    [Fact]
    public void ADayWithFewerPeopleLeftThanTheSmallestBookingIsFull()
    {
        // Two places left, but 55 of 60 people taken and a booking brings at least 10: the UPDATE
        // would refuse every one, so the calendar must not offer the day.
        var slot = Slot(s => (s.BookingMode, s.MaxSponsors) = (BookingMode.Shared, 4));

        Assert.Equal(new SlotDayOpenings(BookingErrors.SlotFull, 2, 5), Openings(slot, SlotDay(slot, bookedSponsors: 2, bookedHeadcount: 55)));
        Assert.Equal(new SlotDayOpenings(null, 2, 10), Openings(slot, SlotDay(slot, bookedSponsors: 2, bookedHeadcount: 50)));
    }

    [Theory]
    [InlineData(SlotInstanceStatus.Full)]
    [InlineData(SlotInstanceStatus.Blocked)]
    [InlineData(SlotInstanceStatus.Closed)]
    public void ADayThatIsNotOpenIsFull(string status)
    {
        var slot = Slot(s => (s.BookingMode, s.MaxSponsors) = (BookingMode.Shared, 4));

        Assert.Equal(BookingErrors.SlotFull, Openings(slot, SlotDay(slot, bookedSponsors: 1, bookedHeadcount: 10, status)).Refused);
    }

    [Fact]
    public void ADayKeepsTheCapacityItWasCreatedWith()
    {
        // Created at 60 people; the slot was edited down to 30 since. A booking may bring at most 30
        // (the slot's rule now), and the day still has its 60 to fill (C3).
        var slot = Slot(s => s.MaxHeadcount = 30);
        Assert.Equal(new SlotDayOpenings(null, 1, 30), Openings(slot, SlotDay(slot, bookedSponsors: 0, bookedHeadcount: 0, maxHeadcount: 60)));

        // Edited up to 100 since a day was created at 40: the day's 40 bound it.
        var bigger = Slot(s => (s.MaxHeadcount, s.BookingMode, s.MaxSponsors) = (100, BookingMode.Shared, 3));
        Assert.Equal(new SlotDayOpenings(null, 2, 25), Openings(bigger, SlotDay(bigger, bookedSponsors: 1, bookedHeadcount: 15, maxHeadcount: 40)));
    }

    [Fact]
    public void TheCalendarGivesTheEnginesReasonBeforeFullness()
    {
        // Blocked and full: the block is the reason, as CanBook would say.
        var slot = Slot();

        Assert.Equal(BookingErrors.DateBlocked, Openings(slot, SlotDay(slot, bookedSponsors: 1, bookedHeadcount: 10), [Block(Friday, Friday)]).Refused);
        Assert.Equal(BookingErrors.TooSoon, MealSlotRules.Openings(slot, MealSlotRules.ActiveInstitution, [], [], Friday, Friday.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), null).Refused);
    }
}

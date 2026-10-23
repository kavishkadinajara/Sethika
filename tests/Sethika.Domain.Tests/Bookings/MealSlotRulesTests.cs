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
}

using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 07, H3 and H5: which year a yearly booking books next, on which day, and from when.</summary>
public class RecurringBookingTests
{
    [Fact]
    public void MadeFromABooking_ItStartsNextYear_OnTheSameDay_WithTheBookingsDetails()
    {
        var booking = new Booking
        {
            DonorUserId = Guid.CreateVersion7(),
            InstitutionId = 7,
            Headcount = 40,
            FulfilmentMode = FulfilmentMode.InstitutionPrepares,
            InMemoryOf = "Amma",
        };
        var publicId = Guid.CreateVersion7();
        var now = new DateTime(2026, 11, 5, 4, 0, 0, DateTimeKind.Utc);

        var yearly = RecurringBooking.From(booking, mealSlotId: 12, new DateOnly(2026, 11, 14), publicId, now);

        Assert.Equal((publicId, booking.DonorUserId!.Value, 7, 12), (yearly.PublicId, yearly.DonorUserId, yearly.InstitutionId, yearly.MealSlotId));
        Assert.Equal((11, 14), (yearly.RecurMonth, yearly.RecurDay));
        Assert.Equal((2027, (short?)2026, (short?)null), (yearly.StartYear, yearly.LastGeneratedYear, yearly.EndYear));
        Assert.Equal((40, FulfilmentMode.InstitutionPrepares, "Amma"), (yearly.Headcount, yearly.FulfilmentMode, yearly.InMemoryOf));
        Assert.True(yearly.IsActive);
        Assert.Equal(now, yearly.CreatedAt);
        Assert.Equal(2027, yearly.NextYear);
    }

    [Fact]
    public void AYearlyBookingNeedsTheDonorsAccount() =>
        Assert.Throws<ArgumentException>(() => RecurringBooking.From(new Booking(), 1, new DateOnly(2026, 11, 14), Guid.CreateVersion7(), DateTime.UtcNow));

    [Theory]
    [InlineData(2027, null, null, 2027)]
    [InlineData(2027, null, 2026, 2027)]
    [InlineData(2027, null, 2027, 2028)]
    [InlineData(2027, null, 2031, 2032)]
    [InlineData(2027, 2029, 2028, 2029)]
    public void TheNextYearIsTheOneAfterTheLastTried_NeverBeforeTheFirst(short start, int? end, int? last, int expected) =>
        Assert.Equal(expected, RecurringBooking.NextYearOf(start, (short?)end, (short?)last, isActive: true));

    [Fact]
    public void AStoppedOneOrOnePastItsLastYearBooksNothingMore()
    {
        Assert.Null(RecurringBooking.NextYearOf(2027, null, 2027, isActive: false));
        Assert.Null(RecurringBooking.NextYearOf(2027, 2029, 2029, isActive: true));
        Assert.Equal(2029, RecurringBooking.NextYearOf(2027, 2029, 2028, isActive: true));
    }

    [Theory]
    [InlineData(2, 29, 2028, "2028-02-29")]
    [InlineData(2, 29, 2027, "2027-02-28")]
    [InlineData(2, 28, 2027, "2027-02-28")]
    [InlineData(12, 31, 2027, "2027-12-31")]
    [InlineData(1, 1, 2030, "2030-01-01")]
    public void ItFallsOnTheSameDay_And29FebruaryOn28InACommonYear(byte month, byte day, int year, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), RecurringBooking.OccursIn(month, day, year));

    [Fact]
    public void ItIsDueFromTheDayTheSlotsWindowReachesIt_NotTheDayBefore()
    {
        var occurrence = new DateOnly(2027, 11, 14);

        Assert.False(RecurringBooking.IsDue(occurrence, maxAdvanceDays: 365, today: occurrence.AddDays(-366)));
        Assert.True(RecurringBooking.IsDue(occurrence, maxAdvanceDays: 365, today: occurrence.AddDays(-365)));
        Assert.True(RecurringBooking.IsDue(occurrence, maxAdvanceDays: 365, today: occurrence.AddDays(-10)));
        Assert.True(RecurringBooking.IsDue(occurrence, maxAdvanceDays: 60, today: occurrence.AddDays(-60)));
        Assert.False(RecurringBooking.IsDue(occurrence, maxAdvanceDays: 60, today: occurrence.AddDays(-61)));
    }

    [Fact]
    public void ItIsDueExactlyWhenTheRulesStopCallingTheDayTooFar()
    {
        var slot = new MealSlot { IsActive = true, MinAdvanceDays = 1, MaxAdvanceDays = 30, StartTime = new TimeOnly(11, 0) };
        var occurrence = new DateOnly(2027, 3, 20);

        foreach (var daysBefore in new[] { 31, 30, 29 })
        {
            var today = occurrence.AddDays(-daysBefore);
            var utcNow = SriLankaTime.ToUtc(today, new TimeOnly(0, 30));
            var tooFar = MealSlotRules.CanBookOn(slot, MealSlotRules.ActiveInstitution, [], [], occurrence, utcNow) == BookingErrors.TooFar;

            Assert.Equal(!tooFar, RecurringBooking.IsDue(occurrence, slot.MaxAdvanceDays, today));
        }
    }
}

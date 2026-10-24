using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 03, C1 and C5: a booking's state machine, and which moves give its place back.</summary>
public class BookingTests
{
    private static readonly Guid Donor = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Staff = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly SlotDay = new(2026, 11, 20);

    private static MealSlot Slot(bool requiresApproval = true) => new()
    {
        Id = 70,
        InstitutionId = 7,
        Name = "Lunch",
        MaxHeadcount = 60,
        RequiresApproval = requiresApproval,
    };

    private static Booking Requested(bool requiresApproval = true) =>
        Booking.Request("BK-2026-000001", Slot(requiresApproval), 900, Donor, "A Donor", "+94771234567", 40, FulfilmentMode.DonorBrings, Now);

    private static Booking Approved()
    {
        var booking = Requested();
        Assert.True(booking.Approve(Staff, Now).Succeeded);
        return booking;
    }

    private static Booking InStatus(string status)
    {
        var booking = status is BookingStatus.Requested or BookingStatus.Rejected ? Requested() : Approved();
        var result = status switch
        {
            BookingStatus.Requested or BookingStatus.Approved => BookingResult.Ok,
            BookingStatus.Rejected => booking.Reject("Full that day", Staff, Now),
            BookingStatus.Cancelled => booking.CancelAsInstitution("Closed", Staff, Now),
            BookingStatus.Completed => booking.Complete(Staff, SlotDay, SlotDay, Now),
            BookingStatus.NoShow => booking.MarkNoShow(Staff, SlotDay, SlotDay, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        Assert.True(result.Succeeded);
        Assert.Equal(status, booking.Status);
        return booking;
    }

    // ------------------------------------------------------------------ making it

    [Fact]
    public void ABookingStartsAsARequestWithItsFirstHistoryRow()
    {
        var booking = Requested();

        Assert.Equal(BookingStatus.Requested, booking.Status);
        Assert.Equal(7, booking.InstitutionId);
        Assert.Equal(900, booking.SlotInstanceId);
        Assert.Equal(Donor, booking.DonorUserId);
        Assert.Equal(Donor, booking.CreatedBy);
        Assert.Equal(Booking.OnlineSource, booking.Source);
        Assert.Null(booking.DecidedAt);

        var first = Assert.Single(booking.StatusHistory);
        Assert.Null(first.FromStatus);
        Assert.Equal(BookingStatus.Requested, first.ToStatus);
    }

    [Fact]
    public void ASlotThatNeedsNoApprovalConfirmsTheBookingAtOnce()
    {
        var booking = Requested(requiresApproval: false);

        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Equal(Now, booking.DecidedAt);
        Assert.Equal(BookingStatus.Approved, Assert.Single(booking.StatusHistory).ToStatus);
    }

    // ------------------------------------------------------------------ deciding

    [Fact]
    public void ARequestCanBeApproved()
    {
        var booking = Requested();

        Assert.True(booking.Approve(Staff, Now).Succeeded);

        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Equal(Staff, booking.DecidedBy);
        Assert.Equal(Now, booking.DecidedAt);
        var last = booking.StatusHistory.Last();
        Assert.Equal((BookingStatus.Requested, BookingStatus.Approved, Staff), (last.FromStatus, last.ToStatus, last.ChangedBy));
    }

    [Fact]
    public void ARejectionCarriesItsReason()
    {
        var booking = Requested();

        Assert.Equal(BookingErrors.ReasonRequired, booking.Reject("  ", Staff, Now).Error);
        Assert.Equal(BookingStatus.Requested, booking.Status);

        Assert.True(booking.Reject("  We are closed for a retreat. ", Staff, Now).Succeeded);

        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal("We are closed for a retreat.", booking.RejectReason);
        Assert.Equal("We are closed for a retreat.", booking.StatusHistory.Last().Comment);
    }

    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public void OnlyARequestCanBeDecided(string status)
    {
        var booking = InStatus(status);
        var history = booking.StatusHistory.Count;

        Assert.Equal(BookingErrors.InvalidTransition, booking.Approve(Staff, Now).Error);
        Assert.Equal(BookingErrors.InvalidTransition, booking.Reject("No", Staff, Now).Error);
        Assert.Equal(status, booking.Status);
        Assert.Equal(history, booking.StatusHistory.Count);
    }

    // ------------------------------------------------------------------ cancelling

    [Theory]
    [InlineData(BookingStatus.Requested)]
    [InlineData(BookingStatus.Approved)]
    public void TheDonorCanCancelUntilTheDayBefore(string status)
    {
        var booking = InStatus(status);

        Assert.True(booking.CancelAsDonor(null, Donor, SlotDay, SlotDay.AddDays(-1), Now).Succeeded);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(Donor, booking.CancelledBy);
        Assert.Equal(Now, booking.CancelledAt);
        Assert.Null(booking.CancelReason);
    }

    [Fact]
    public void OnTheDayItselfOnlyTheInstitutionCanCancel()
    {
        var booking = Approved();

        Assert.Equal(BookingErrors.CancelWindowClosed, booking.CancelAsDonor("Sick", Donor, SlotDay, SlotDay, Now).Error);
        Assert.Equal(BookingErrors.CancelWindowClosed, booking.CancelAsDonor("Sick", Donor, SlotDay, SlotDay.AddDays(1), Now).Error);
        Assert.Equal(BookingStatus.Approved, booking.Status);

        Assert.True(booking.CancelAsInstitution("The donor phoned in sick", Staff, Now).Succeeded);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("The donor phoned in sick", booking.CancelReason);
    }

    /// <summary>The cancel button on "my bookings" is shown exactly when the donor's cancel would succeed.</summary>
    [Theory]
    [InlineData(BookingStatus.Requested)]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public void TheCancelButtonAgreesWithTheCancel(string status)
    {
        foreach (var daysBefore in new[] { 30, 1, 0, -1 })
        {
            var today = SlotDay.AddDays(-daysBefore);
            var cancelled = InStatus(status).CancelAsDonor(null, Donor, SlotDay, today, Now).Succeeded;

            Assert.Equal(cancelled, Booking.DonorMayCancel(status, SlotDay, today));
        }

        Assert.Equal(status is BookingStatus.Requested or BookingStatus.Approved, Booking.DonorMayCancel(status, SlotDay, SlotDay.AddDays(-1)));
    }

    [Fact]
    public void NobodyButTheDonorCancelsAsTheDonor()
    {
        var booking = Approved();

        Assert.Equal(BookingErrors.NotDonor, booking.CancelAsDonor(null, Stranger, SlotDay, SlotDay.AddDays(-5), Now).Error);
        Assert.Equal(BookingStatus.Approved, booking.Status);
    }

    [Fact]
    public void TheInstitutionSaysWhyItCancels() =>
        Assert.Equal(BookingErrors.ReasonRequired, Approved().CancelAsInstitution("", Staff, Now).Error);

    [Fact]
    public void AReasonFitsItsColumn()
    {
        // Three hundred characters fit NVARCHAR(300); one more would fail the save, not the rule.
        var longest = new string('x', Booking.MaxReason);
        var tooLong = longest + "x";

        Assert.Equal(BookingErrors.ReasonTooLong, Requested().Reject(tooLong, Staff, Now).Error);
        Assert.Equal(BookingErrors.ReasonTooLong, Approved().CancelAsInstitution(tooLong, Staff, Now).Error);
        Assert.Equal(BookingErrors.ReasonTooLong, Approved().CancelAsDonor(tooLong, Donor, SlotDay, SlotDay.AddDays(-5), Now).Error);

        // Measured after trimming, as it is stored.
        Assert.True(Requested().Reject("  " + longest + "  ", Staff, Now).Succeeded);
        Assert.True(Approved().CancelAsInstitution(longest, Staff, Now).Succeeded);
        Assert.True(Approved().CancelAsDonor(longest, Donor, SlotDay, SlotDay.AddDays(-5), Now).Succeeded);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public void AFinishedBookingCannotBeCancelled(string status)
    {
        var booking = InStatus(status);

        Assert.Equal(BookingErrors.InvalidTransition, booking.CancelAsDonor(null, Donor, SlotDay, SlotDay.AddDays(-5), Now).Error);
        Assert.Equal(BookingErrors.InvalidTransition, booking.CancelAsInstitution("Why", Staff, Now).Error);
        Assert.Equal(status, booking.Status);
    }

    // ------------------------------------------------------------------ after the meal

    [Fact]
    public void AMealIsCompletedOrMissedOnlyFromItsDay()
    {
        var early = Approved();
        Assert.Equal(BookingErrors.TooEarly, early.Complete(Staff, SlotDay, SlotDay.AddDays(-1), Now).Error);
        Assert.Equal(BookingErrors.TooEarly, early.MarkNoShow(Staff, SlotDay, SlotDay.AddDays(-1), Now).Error);
        Assert.Equal(BookingStatus.Approved, early.Status);

        var completed = Approved();
        Assert.True(completed.Complete(Staff, SlotDay, SlotDay, Now).Succeeded);
        Assert.Equal(BookingStatus.Completed, completed.Status);
        Assert.Equal(Now, completed.CompletedAt);

        var missed = Approved();
        Assert.True(missed.MarkNoShow(Staff, SlotDay, SlotDay.AddDays(3), Now).Succeeded);
        Assert.Equal(BookingStatus.NoShow, missed.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Requested)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public void OnlyAnApprovedBookingCanBeClosed(string status)
    {
        var booking = InStatus(status);

        Assert.Equal(BookingErrors.InvalidTransition, booking.Complete(Staff, SlotDay, SlotDay, Now).Error);
        Assert.Equal(BookingErrors.InvalidTransition, booking.MarkNoShow(Staff, SlotDay, SlotDay, Now).Error);
    }

    // ------------------------------------------------------------------ the place on the slot-day (C1)

    [Theory]
    [InlineData(BookingStatus.Requested, true)]
    [InlineData(BookingStatus.Approved, true)]
    [InlineData(BookingStatus.Completed, true)]
    [InlineData(BookingStatus.NoShow, true)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    public void OnlyARejectionOrACancellationGivesThePlaceBack(string status, bool holds) =>
        Assert.Equal(holds, BookingStatus.HoldsPlace(status));

    [Fact]
    public void EveryMoveIsInTheHistoryInOrder()
    {
        var booking = Approved();
        Assert.True(booking.Complete(Staff, SlotDay, SlotDay, Now).Succeeded);

        Assert.Equal(
            [(null, BookingStatus.Requested), (BookingStatus.Requested, BookingStatus.Approved), (BookingStatus.Approved, BookingStatus.Completed)],
            booking.StatusHistory.Select(h => (h.FromStatus, h.ToStatus)));
    }
}

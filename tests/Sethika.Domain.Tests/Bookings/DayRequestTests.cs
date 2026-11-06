using Sethika.Domain.Bookings;

namespace Sethika.Domain.Tests.Bookings;

/// <summary>Plan 08, K6: a day request is answered once, and its answer is what <c>CK_DayRequests_Decision</c> wants.</summary>
public class DayRequestTests
{
    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Giving_NamesTheDayLetGo_AndTheBookingMade()
    {
        var request = new DayRequest();

        Assert.True(request.Give(4, 99, Staff, Now));

        Assert.Equal((DayRequest.Given, (int?)4, (long?)99, (Guid?)Staff, (DateTime?)Now), (request.Status, request.StandingSponsorshipId, request.BookingId, request.DecidedBy, request.DecidedAt));
        Assert.False(request.IsOpen);
    }

    [Fact]
    public void Declining_KeepsAReason_TrimmedOrNone()
    {
        var request = new DayRequest();

        Assert.True(request.Decline("  Given for years  ", Staff, Now));

        Assert.Equal((DayRequest.Declined, "Given for years", (int?)null, (long?)null), (request.Status, request.DeclineReason, request.StandingSponsorshipId, request.BookingId));

        var silent = new DayRequest();
        silent.Decline("   ", Staff, Now);
        Assert.Null(silent.DeclineReason);
    }

    [Fact]
    public void AnAnsweredRequest_IsNotAnsweredAgain()
    {
        var given = new DayRequest();
        given.Give(4, 99, Staff, Now);

        Assert.False(given.Decline("no", Staff, Now));
        Assert.False(given.Give(5, 100, Staff, Now));
        Assert.Equal((DayRequest.Given, (int?)4, (string?)null), (given.Status, given.StandingSponsorshipId, given.DeclineReason));

        var declined = new DayRequest();
        declined.Decline(null, Staff, Now);
        Assert.False(declined.Give(4, 99, Staff, Now));
        Assert.Equal((DayRequest.Declined, (long?)null), (declined.Status, declined.BookingId));
    }
}

using Sethika.Domain.Giving;

namespace Sethika.Domain.Tests.Giving;

/// <summary>Plan 09, M6: a gift is voided once, by someone other than its recorder, with a reason.</summary>
public class DonationTests
{
    private static readonly Guid Recorder = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Checker = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTime Now = new(2026, 11, 4, 6, 0, 0, DateTimeKind.Utc);

    private static Donation Gift() => new() { Kind = Donation.Money, Amount = 5000m, DonorName = "Sunil", ReceiptNo = "RCP-2026-000001", RecordedBy = Recorder };

    [Fact]
    public void ASecondPersonVoids_WithWhoWhyAndWhen()
    {
        var gift = Gift();

        Assert.Null(gift.Void(Checker, "  Recorded twice.  ", Now));
        Assert.True(gift.IsVoided);
        Assert.Equal(Checker, gift.VoidedBy);
        Assert.Equal("Recorded twice.", gift.VoidReason);
        Assert.Equal(Now, gift.VoidedAt);
    }

    [Fact]
    public void TheRecorder_CannotVoidTheirOwnGift()
    {
        var gift = Gift();

        Assert.Equal(DonationErrors.VoidOwnRecord, gift.Void(Recorder, "Mistake.", Now));
        Assert.False(gift.IsVoided);
        Assert.Null(gift.VoidedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AVoid_NeedsAReason(string? reason)
    {
        var gift = Gift();

        Assert.Equal(DonationErrors.InvalidReason, gift.Void(Checker, reason, Now));
        Assert.False(gift.IsVoided);
    }

    [Fact]
    public void AReason_IsAtMost300Characters()
    {
        Assert.Null(Gift().Void(Checker, new string('x', Donation.MaxReason), Now));
        Assert.Equal(DonationErrors.InvalidReason, Gift().Void(Checker, new string('x', Donation.MaxReason + 1), Now));
    }

    [Fact]
    public void AVoidedGift_IsNeverVoidedAgain()
    {
        var gift = Gift();
        Assert.Null(gift.Void(Checker, "First.", Now));

        Assert.Equal(DonationErrors.AlreadyVoid, gift.Void(Checker, "Second.", Now.AddHours(1)));
        Assert.Equal("First.", gift.VoidReason);
        Assert.Equal(Now, gift.VoidedAt);
    }
}

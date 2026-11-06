using Sethika.Domain.Giving;

namespace Sethika.Domain.Tests.Giving;

/// <summary>A pledge is settled once, delivered or cancelled (plan 09, M13, M15).</summary>
public sealed class PledgeTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 6, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Staff = Guid.NewGuid();

    private static Pledge Open(decimal quantity = 10m) => new() { Quantity = quantity };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.01)]
    [InlineData(1.234)]
    public void AQuantityOutsideThePledge_IsRefused_AndChangesNothing(decimal quantity)
    {
        var pledge = Open();

        Assert.Equal(NeedErrors.InvalidQuantity, pledge.Receive(Staff, quantity, Now));
        Assert.Equal(PledgeStatus.Pledged, pledge.Status);
        Assert.Null(pledge.ReceivedQuantity);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(0.01)]
    public void Receiving_RecordsWhoAndHowMuch(decimal quantity)
    {
        var pledge = Open();

        Assert.Null(pledge.Receive(Staff, quantity, Now));
        Assert.Equal(PledgeStatus.Delivered, pledge.Status);
        Assert.Equal(quantity, pledge.ReceivedQuantity);
        Assert.Equal(Staff, pledge.ReceivedBy);
        Assert.Equal(Now, pledge.ReceivedAt);
    }

    [Fact]
    public void ASettledPledge_StaysSettled()
    {
        var delivered = Open();
        delivered.Receive(Staff, 1m, Now);
        var cancelled = Open();
        cancelled.Cancel(Staff, "Why.", reasonRequired: true, Now);

        Assert.Equal(NeedErrors.PledgeNotOpen, delivered.Receive(Staff, 1m, Now));
        Assert.Equal(NeedErrors.PledgeNotOpen, delivered.Cancel(Staff, null, reasonRequired: false, Now));
        Assert.Equal(NeedErrors.PledgeNotOpen, cancelled.Receive(Staff, 1m, Now));
        Assert.Equal(NeedErrors.PledgeNotOpen, cancelled.Cancel(Staff, null, reasonRequired: false, Now));
    }

    [Fact]
    public void StaffMustSayWhy_ADonorNeedNot()
    {
        var byStaff = Open();
        Assert.Equal(NeedErrors.InvalidReason, byStaff.Cancel(Staff, "  ", reasonRequired: true, Now));
        Assert.Equal(PledgeStatus.Pledged, byStaff.Status);

        var byDonor = Open();
        Assert.Null(byDonor.Cancel(Staff, "  ", reasonRequired: false, Now));
        Assert.Equal(PledgeStatus.Cancelled, byDonor.Status);
        Assert.Null(byDonor.CancelReason);
        Assert.Equal(Now, byDonor.CancelledAt);
    }

    [Fact]
    public void AReasonTooLong_IsRefused()
    {
        Assert.Equal(NeedErrors.InvalidReason, Open().Cancel(Staff, new string('x', Pledge.MaxReason + 1), reasonRequired: true, Now));
        Assert.Null(Open().Cancel(Staff, new string('x', Pledge.MaxReason), reasonRequired: true, Now));
    }
}

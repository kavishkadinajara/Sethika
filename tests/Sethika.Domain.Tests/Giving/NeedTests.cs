using Sethika.Domain.Giving;

namespace Sethika.Domain.Tests.Giving;

/// <summary>A need's steps (plan 09, M11, M29). The counters' own moves are SQL, tested in NeedEndpointsTests.</summary>
public sealed class NeedTests
{
    private static Need Draft() => new() { QuantityNeeded = 10m };

    private static Need Opened()
    {
        var need = Draft();
        need.Publish();
        return need;
    }

    [Fact]
    public void ADraftOpens_Once()
    {
        var need = Draft();

        Assert.Null(need.Publish());
        Assert.Equal(NeedStatus.Open, need.Status);
        Assert.Equal(NeedErrors.WrongStatus, need.Publish());
    }

    [Fact]
    public void ADraftCannotBeClosed_ButCanBeCancelled()
    {
        Assert.Equal(NeedErrors.WrongStatus, Draft().Close());

        var need = Draft();
        Assert.Null(need.Cancel());
        Assert.Equal(NeedStatus.Cancelled, need.Status);
    }

    [Fact]
    public void ASettledNeed_TakesNoMoreSteps()
    {
        var closed = Opened();
        closed.Close();
        var cancelled = Opened();
        cancelled.Cancel();

        Assert.False(closed.IsEditable);
        Assert.False(cancelled.IsEditable);
        Assert.Equal(NeedErrors.WrongStatus, closed.Close());
        Assert.Equal(NeedErrors.WrongStatus, closed.Cancel());
        Assert.Equal(NeedErrors.WrongStatus, cancelled.Close());
        Assert.Equal(NeedErrors.WrongStatus, cancelled.Publish());
    }

    [Fact]
    public void AQuantityChange_OnAnEmptyNeed_NeitherFulfilsNorRefuses()
    {
        var need = Opened();

        Assert.Null(need.ChangeQuantity(4m));
        Assert.Equal(4m, need.QuantityNeeded);
        Assert.Equal(NeedStatus.Open, need.Status);
    }
}

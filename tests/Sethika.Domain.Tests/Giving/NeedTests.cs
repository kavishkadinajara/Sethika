using Sethika.Domain.Giving;

namespace Sethika.Domain.Tests.Giving;

/// <summary>A need's steps (plan 09, M11, M29). The counters' own moves are SQL, tested in NeedEndpointsTests.</summary>
public sealed class NeedTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 4, 0, 0, DateTimeKind.Utc);

    private static Need Draft() => new() { QuantityNeeded = 10m };

    private static Need Opened()
    {
        var need = Draft();
        need.Publish(Now);
        return need;
    }

    [Fact]
    public void ADraftOpens_Once()
    {
        var need = Draft();

        Assert.Null(need.Publish(Now));
        Assert.Equal(NeedStatus.Open, need.Status);
        Assert.Equal(NeedErrors.WrongStatus, need.Publish(Now));
    }

    [Fact]
    public void ADraftCannotBeClosed_ButCanBeCancelled()
    {
        Assert.Equal(NeedErrors.WrongStatus, Draft().Close(Now));

        var need = Draft();
        Assert.Null(need.Cancel(Now));
        Assert.Equal(NeedStatus.Cancelled, need.Status);
    }

    [Fact]
    public void ASettledNeed_TakesNoMoreSteps()
    {
        var closed = Opened();
        closed.Close(Now);
        var cancelled = Opened();
        cancelled.Cancel(Now);

        Assert.False(closed.IsEditable);
        Assert.False(cancelled.IsEditable);
        Assert.Equal(NeedErrors.WrongStatus, closed.Close(Now));
        Assert.Equal(NeedErrors.WrongStatus, closed.Cancel(Now));
        Assert.Equal(NeedErrors.WrongStatus, cancelled.Close(Now));
        Assert.Equal(NeedErrors.WrongStatus, cancelled.Publish(Now));
    }

    [Fact]
    public void AQuantityChange_OnAnEmptyNeed_NeitherFulfilsNorRefuses()
    {
        var need = Opened();

        Assert.Null(need.ChangeQuantity(4m, Now));
        Assert.Equal(4m, need.QuantityNeeded);
        Assert.Equal(NeedStatus.Open, need.Status);
    }

    [Fact]
    public void Publishing_DatesTheOpening()
    {
        var need = Draft();

        need.Publish(Now);

        Assert.Equal(Now, need.OpenedAt);
        Assert.Null(need.EndedAt);
    }

    [Fact]
    public void ARefusedStep_DatesNothing()
    {
        var need = Opened();
        var opened = need.OpenedAt;

        Assert.NotNull(need.Publish(Now.AddDays(1)));
        Assert.Equal(opened, need.OpenedAt);

        Assert.NotNull(Draft().Close(Now));
        Assert.Null(Draft().EndedAt);
    }

    [Fact]
    public void ClosingAndCancelling_DateTheEnd()
    {
        var closed = Opened();
        closed.Close(Now);
        Assert.Equal(Now, closed.EndedAt);

        var cancelled = Opened();
        cancelled.Cancel(Now);
        Assert.Equal(Now, cancelled.EndedAt);
    }
}

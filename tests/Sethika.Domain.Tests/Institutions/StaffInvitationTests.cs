using Sethika.Domain.Institutions;

namespace Sethika.Domain.Tests.Institutions;

/// <summary>Plan 04, X4-X5: an invitation's state machine, who may answer it, and its expiry.</summary>
public class StaffInvitationTests
{
    private static readonly Guid Invitee = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    private static StaffInvitation Pending() =>
        StaffInvitation.Invite(7, 70, Invitee, "A Volunteer", "+94771234567", new string('a', 64), Owner, Now);

    [Fact]
    public void ANewInvitationIsPendingForSevenDays()
    {
        var invitation = Pending();

        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.NotEqual(Guid.Empty, invitation.PublicId);
        Assert.True(invitation.IsOpen(Now.AddDays(7).AddTicks(-1)));
        Assert.False(invitation.IsOpen(Now.AddDays(7)));
    }

    [Fact]
    public void TheInviteeAcceptsIt()
    {
        var invitation = Pending();

        Assert.True(invitation.Accept(Invitee, Now.AddHours(1)).Succeeded);

        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal(Invitee, invitation.AcceptedUserId);
        Assert.Equal(Now.AddHours(1), invitation.AcceptedAt);
        Assert.False(invitation.IsOpen(Now.AddHours(2)));
    }

    [Fact]
    public void NobodyElseCanAcceptOrDeclineIt()
    {
        var invitation = Pending();

        Assert.Equal(InvitationErrors.NotYours, invitation.Accept(Stranger, Now).Error);
        Assert.Equal(InvitationErrors.NotYours, invitation.Decline(Owner, Now).Error);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Null(invitation.AcceptedUserId);
    }

    [Fact]
    public void AnExpiredInvitationCannotBeAnswered()
    {
        var invitation = Pending();
        var late = Now + StaffInvitation.Lifetime;

        Assert.Equal(InvitationErrors.Expired, invitation.Accept(Invitee, late).Error);
        Assert.Equal(InvitationErrors.Expired, invitation.Decline(Invitee, late).Error);
        Assert.Equal(InvitationErrors.Expired, invitation.Revoke(Owner, late).Error);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public void TheInviteeDeclinesIt()
    {
        var invitation = Pending();

        Assert.True(invitation.Decline(Invitee, Now).Succeeded);

        Assert.Equal(InvitationStatus.Declined, invitation.Status);
        Assert.Equal(Invitee, invitation.ClosedBy);
        Assert.Null(invitation.AcceptedUserId);
    }

    [Fact]
    public void TheInstitutionRevokesIt()
    {
        var invitation = Pending();

        Assert.True(invitation.Revoke(Owner, Now).Succeeded);

        Assert.Equal(InvitationStatus.Revoked, invitation.Status);
        Assert.Equal(Owner, invitation.ClosedBy);
    }

    [Fact]
    public void OnlyAPendingInvitationPastItsTimeExpires()
    {
        var open = Pending();
        Assert.False(open.Expire(Now.AddDays(7).AddTicks(-1)));
        Assert.Equal(InvitationStatus.Pending, open.Status);

        Assert.True(open.Expire(Now.AddDays(7)));
        Assert.Equal(InvitationStatus.Expired, open.Status);

        var accepted = Pending();
        Assert.True(accepted.Accept(Invitee, Now).Succeeded);
        Assert.False(accepted.Expire(Now.AddDays(30)));
        Assert.Equal(InvitationStatus.Accepted, accepted.Status);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("revoke")]
    public void AClosedInvitationStaysClosed(string closedBy)
    {
        var invitation = Pending();
        var first = closedBy switch
        {
            "accept" => invitation.Accept(Invitee, Now),
            "decline" => invitation.Decline(Invitee, Now),
            _ => invitation.Revoke(Owner, Now),
        };
        Assert.True(first.Succeeded);
        var status = invitation.Status;

        Assert.Equal(InvitationErrors.NotPending, invitation.Accept(Invitee, Now).Error);
        Assert.Equal(InvitationErrors.NotPending, invitation.Decline(Invitee, Now).Error);
        Assert.Equal(InvitationErrors.NotPending, invitation.Revoke(Owner, Now).Error);
        Assert.Equal(status, invitation.Status);
    }
}

using Sethika.Domain.Services;

namespace Sethika.Domain.Tests.Services;

/// <summary>Phase E, section 4 and V6: the appointment's transitions and its leaders' answers.</summary>
public class ServiceAppointmentTests
{
    private static readonly Guid Donor = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid Admin = Guid.Parse("0199a000-0000-7000-8000-000000000002");
    private static readonly Guid Someone = Guid.Parse("0199a000-0000-7000-8000-000000000003");

    /// <summary>Friday 2026-11-20, 10:00 in Colombo; a three-hour service.</summary>
    private static readonly DateTime StartsAt = new(2026, 11, 20, 4, 30, 0, DateTimeKind.Utc);

    /// <summary>Wednesday 2026-11-18, 09:00 in Colombo.</summary>
    private static readonly DateTime Wednesday = new(2026, 11, 18, 3, 30, 0, DateTimeKind.Utc);

    private static ServiceType Pirith() => new()
    {
        Id = 5,
        InstitutionId = 7,
        Name = "Pirith",
        DurationMinutes = 180,
        LeadersRequired = 2,
        LocationMode = ServiceLocation.Either,
    };

    private static ServiceAppointment Requested() =>
        ServiceAppointment.Request("APT-2026-000001", Pirith(), StartsAt, ServiceLocation.AtDonorPlace, " 12 Temple Road ", "Almsgiving", null, Donor, "Nimal", "+94771234567", Wednesday);

    private static ServiceAppointment Approved(params int[] leaders)
    {
        var appointment = Requested();
        Assert.True(appointment.Assign(leaders.Length == 0 ? [1] : leaders, 2, Admin, Wednesday).Succeeded);
        return appointment;
    }

    [Fact]
    public void ARequestEndsWhenItsServiceDoesAndHoldsNoLeader()
    {
        var appointment = Requested();

        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.Equal(StartsAt.AddHours(3), appointment.EndsAt);
        Assert.Equal("12 Temple Road", appointment.Address);
        Assert.Empty(appointment.Leaders);
        Assert.Equal(7, appointment.InstitutionId);
        var first = Assert.Single(appointment.StatusHistory);
        Assert.Null(first.FromStatus);
    }

    [Fact]
    public void AnAddressIsKeptOnlyForAVisitHome()
    {
        var atTemple = ServiceAppointment.Request("APT-2026-000002", Pirith(), StartsAt, ServiceLocation.AtInstitution, "12 Temple Road", null, null, Donor, "Nimal", "+94771234567", Wednesday);

        Assert.Null(atTemple.Address);
    }

    [Fact]
    public void AssigningTheFirstLeadersApprovesTheRequest()
    {
        var appointment = Requested();

        Assert.True(appointment.Assign([1, 2], 2, Admin, Wednesday).Succeeded);

        Assert.Equal(AppointmentStatus.Approved, appointment.Status);
        Assert.Equal(Admin, appointment.DecidedBy);
        Assert.All(appointment.Leaders, l => Assert.Equal(AssignmentResponse.Pending, l.Response));
        Assert.Equal(AppointmentStatus.Approved, appointment.StatusHistory.Last().ToStatus);
    }

    [Fact]
    public void AddingALeaderLaterDoesNotWriteAnotherApproval()
    {
        var appointment = Approved(1);
        var history = appointment.StatusHistory.Count;

        Assert.True(appointment.Assign([2], 2, Admin, Wednesday).Succeeded);

        Assert.Equal(history, appointment.StatusHistory.Count);
        Assert.Equal(2, appointment.LiveLeaders.Count());
    }

    [Fact]
    public void NoMoreLeadersThanTheServiceNeeds()
    {
        Assert.Equal(ServiceErrors.TooManyLeaders, Requested().Assign([1, 2, 3], 2, Admin, Wednesday).Error);
        Assert.Equal(ServiceErrors.TooManyLeaders, Approved(1, 2).Assign([3], 2, Admin, Wednesday).Error);
    }

    [Fact]
    public void ALeaderCannotBeAssignedTwice()
    {
        Assert.Equal(ServiceErrors.AlreadyAssigned, Approved(1).Assign([1], 2, Admin, Wednesday).Error);
        Assert.Equal(ServiceErrors.AlreadyAssigned, Requested().Assign([1, 1], 2, Admin, Wednesday).Error);
    }

    [Fact]
    public void ALeaderWhoDeclinedCanBeAskedAgainOnTheSameRow()
    {
        var appointment = Approved(1);
        Assert.True(appointment.Respond(1, accept: false, "Away", Someone, onBehalf: false, Wednesday).Succeeded);

        Assert.True(appointment.Assign([1], 2, Admin, Wednesday).Succeeded);

        var row = Assert.Single(appointment.Leaders);
        Assert.Equal(AssignmentResponse.Pending, row.Response);
        Assert.Null(row.DeclineReason);
        Assert.Null(row.RespondedAt);
    }

    [Fact]
    public void NobodyIsAssignedToAFinishedOrClosedAppointment()
    {
        Assert.Equal(ServiceErrors.AlreadyOver, Requested().Assign([1], 2, Admin, StartsAt.AddHours(3)).Error);

        var rejected = Requested();
        rejected.Reject("No monks free", Admin, Wednesday);
        Assert.Equal(ServiceErrors.InvalidTransition, rejected.Assign([1], 2, Admin, Wednesday).Error);
        Assert.Equal(ServiceErrors.InvalidTransition, Requested().Assign([], 2, Admin, Wednesday).Error);
    }

    [Fact]
    public void ADeclineMakesTheAppointmentNeedALeaderAgain()
    {
        var appointment = Approved(1, 2);
        Assert.False(appointment.NeedsLeaders(2));

        Assert.True(appointment.Respond(2, accept: false, null, Someone, onBehalf: false, Wednesday).Succeeded);

        Assert.True(appointment.NeedsLeaders(2));
        Assert.Equal(AppointmentStatus.Approved, appointment.Status);
    }

    [Fact]
    public void AnAcceptedLeaderMayStillDeclineButNotAcceptTwice()
    {
        var appointment = Approved(1);
        Assert.True(appointment.Respond(1, accept: true, null, Someone, onBehalf: false, Wednesday).Succeeded);

        Assert.Equal(ServiceErrors.InvalidTransition, appointment.Respond(1, accept: true, null, Someone, onBehalf: false, Wednesday).Error);
        Assert.True(appointment.Respond(1, accept: false, "Ill", Someone, onBehalf: false, Wednesday).Succeeded);
        Assert.Equal("Ill", appointment.Leaders.Single().DeclineReason);
    }

    [Fact]
    public void AnAdministratorsAnswerIsMarkedAsOnBehalf()
    {
        var appointment = Approved(1);

        Assert.True(appointment.Respond(1, accept: true, null, Admin, onBehalf: true, Wednesday).Succeeded);

        Assert.True(appointment.Leaders.Single().RespondedOnBehalf);
        Assert.Equal(Admin, appointment.Leaders.Single().RespondedBy);
    }

    [Fact]
    public void OnlyALiveLeaderOfAnApprovedAppointmentAnswers()
    {
        Assert.Equal(ServiceErrors.NotAssigned, Approved(1).Respond(9, accept: true, null, Someone, onBehalf: false, Wednesday).Error);
        Assert.Equal(ServiceErrors.InvalidTransition, Requested().Respond(1, accept: true, null, Someone, onBehalf: false, Wednesday).Error);

        var removed = Approved(1);
        Assert.True(removed.Remove(1, Admin, Wednesday).Succeeded);
        Assert.Equal(ServiceErrors.NotAssigned, removed.Respond(1, accept: true, null, Someone, onBehalf: false, Wednesday).Error);
        Assert.Equal(ServiceErrors.NotAssigned, removed.Remove(1, Admin, Wednesday).Error);
    }

    [Fact]
    public void NobodyAnswersOnceTheServiceIsOver()
    {
        var appointment = Approved(1);

        Assert.Equal(ServiceErrors.AlreadyOver, appointment.Respond(1, accept: false, null, Someone, onBehalf: false, StartsAt.AddHours(3)).Error);
        Assert.True(appointment.Respond(1, accept: true, null, Someone, onBehalf: false, StartsAt.AddHours(3).AddMinutes(-1)).Succeeded);
    }

    [Fact]
    public void RejectingNeedsAReasonAndOnlyARequestCanBeRejected()
    {
        Assert.Equal(ServiceErrors.ReasonRequired, Requested().Reject(" ", Admin, Wednesday).Error);
        Assert.Equal(ServiceErrors.ReasonTooLong, Requested().Reject(new string('x', 301), Admin, Wednesday).Error);
        Assert.Equal(ServiceErrors.InvalidTransition, Approved().Reject("Too late", Admin, Wednesday).Error);

        var appointment = Requested();
        Assert.True(appointment.Reject(" No monks free ", Admin, Wednesday).Succeeded);
        Assert.Equal("No monks free", appointment.RejectReason);
        Assert.Equal(AppointmentStatus.Rejected, appointment.Status);
    }

    [Fact]
    public void TheDonorCancelsUntilTheEndOfTheDayBeforeInColombo()
    {
        // Thursday 23:59 in Colombo is 18:29 UTC Thursday: still the day before.
        var thursdayLate = new DateTime(2026, 11, 19, 18, 29, 0, DateTimeKind.Utc);
        // Friday 00:00 in Colombo is 18:30 UTC Thursday: the day itself.
        var fridayMidnight = new DateTime(2026, 11, 19, 18, 30, 0, DateTimeKind.Utc);

        Assert.True(Requested().CancelAsDonor(null, Donor, thursdayLate).Succeeded);
        Assert.Equal(ServiceErrors.CancelWindowClosed, Approved().CancelAsDonor(null, Donor, fridayMidnight).Error);
    }

    [Fact]
    public void NobodyButTheDonorCancelsAsTheDonor()
    {
        Assert.Equal(ServiceErrors.NotDonor, Requested().CancelAsDonor(null, Someone, Wednesday).Error);
    }

    [Fact]
    public void TheInstitutionCancelsWithAReasonUntilTheServiceEnds()
    {
        Assert.Equal(ServiceErrors.ReasonRequired, Approved().CancelAsInstitution("", Admin, Wednesday).Error);
        Assert.True(Approved().CancelAsInstitution("Flooding", Admin, StartsAt.AddHours(2)).Succeeded);
        Assert.Equal(ServiceErrors.AlreadyOver, Approved().CancelAsInstitution("Flooding", Admin, StartsAt.AddHours(3)).Error);

        var cancelled = Requested();
        cancelled.CancelAsDonor(null, Donor, Wednesday);
        Assert.Equal(ServiceErrors.InvalidTransition, cancelled.CancelAsInstitution("Again", Admin, Wednesday).Error);
    }

    [Fact]
    public void AServiceIsCompletedOnlyAfterItEnds()
    {
        Assert.Equal(ServiceErrors.TooEarly, Approved().Complete(Admin, StartsAt.AddHours(3).AddMinutes(-1)).Error);
        Assert.Equal(ServiceErrors.InvalidTransition, Requested().Complete(Admin, StartsAt.AddHours(4)).Error);

        var appointment = Approved();
        Assert.True(appointment.Complete(Admin, StartsAt.AddHours(3)).Succeeded);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Fact]
    public void TheCancelButtonAgreesWithTheCancel()
    {
        Assert.True(ServiceAppointment.DonorMayCancel(AppointmentStatus.Requested, StartsAt, Wednesday));
        Assert.False(ServiceAppointment.DonorMayCancel(AppointmentStatus.Completed, StartsAt, Wednesday));
        Assert.False(ServiceAppointment.DonorMayCancel(AppointmentStatus.Approved, StartsAt, StartsAt.AddHours(-1)));
    }

    [Fact]
    public void EveryStatusChangeIsRecordedWithItsReason()
    {
        var appointment = Approved();
        appointment.CancelAsInstitution("Flooding", Admin, Wednesday);

        Assert.Equal(
            [(null, AppointmentStatus.Requested), (AppointmentStatus.Requested, AppointmentStatus.Approved), (AppointmentStatus.Approved, AppointmentStatus.Cancelled)],
            appointment.StatusHistory.Select(h => (h.FromStatus, h.ToStatus)));
        Assert.Equal("Flooding", appointment.StatusHistory.Last().Comment);
    }
}

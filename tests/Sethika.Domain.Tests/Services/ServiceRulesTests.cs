using Sethika.Domain.Bookings;
using Sethika.Domain.Services;

namespace Sethika.Domain.Tests.Services;

/// <summary>Phase E, section 4, flow 1: every reason a service request is refused, in the order checked.</summary>
public class ServiceRulesTests
{
    private const string Active = MealSlotRules.ActiveInstitution;

    /// <summary>Thursday 2026-11-19, 09:00 in Colombo.</summary>
    private static readonly DateTime ThursdayMorningUtc = new(2026, 11, 19, 3, 30, 0, DateTimeKind.Utc);

    /// <summary>Friday 2026-11-20, 10:00 in Colombo: the next day.</summary>
    private static readonly DateTime FridayTenUtc = new(2026, 11, 20, 4, 30, 0, DateTimeKind.Utc);

    private static ServiceType Type(Action<ServiceType>? change = null)
    {
        var type = new ServiceType
        {
            Id = 5,
            InstitutionId = 7,
            Name = "Pirith",
            DurationMinutes = 180,
            BufferMinutes = 30,
            LeadersRequired = 2,
            LocationMode = ServiceLocation.Either,
            MinAdvanceDays = 1,
            MaxAdvanceDays = 30,
        };
        change?.Invoke(type);
        return type;
    }

    private static string? CanRequest(
        ServiceType type,
        DateTime? startsAt = null,
        string location = ServiceLocation.AtInstitution,
        string? address = null,
        string status = Active,
        DateTime? now = null) =>
        ServiceRules.CanRequest(type, status, startsAt ?? FridayTenUtc, location, address, now ?? ThursdayMorningUtc);

    [Fact]
    public void AnOrdinaryRequestMayBeMade()
    {
        Assert.Null(CanRequest(Type()));
        Assert.Null(CanRequest(Type(), location: ServiceLocation.AtDonorPlace, address: "12 Temple Road, Galle"));
    }

    [Fact]
    public void AnInactiveServiceIsRefusedFirst()
    {
        Assert.Equal(ServiceErrors.ServiceInactive, CanRequest(Type(t => t.IsActive = false), status: "Suspended"));
    }

    [Fact]
    public void OnlyAnActiveInstitutionTakesRequests()
    {
        Assert.Equal(ServiceErrors.InstitutionNotActive, CanRequest(Type(), status: "Suspended"));
    }

    [Fact]
    public void TheStartMustBeOnTheQuarterHour()
    {
        Assert.Equal(ServiceErrors.NotOnTheGrid, CanRequest(Type(), startsAt: FridayTenUtc.AddMinutes(10)));
    }

    [Fact]
    public void OnlyAnOfferedPlaceCanBeAskedFor()
    {
        Assert.Equal(ServiceErrors.LocationNotOffered, CanRequest(Type(t => t.LocationMode = ServiceLocation.AtInstitution), location: ServiceLocation.AtDonorPlace, address: "Galle"));
        Assert.Equal(ServiceErrors.LocationNotOffered, CanRequest(Type(t => t.LocationMode = ServiceLocation.AtDonorPlace)));
        Assert.Equal(ServiceErrors.LocationNotOffered, CanRequest(Type(), location: ServiceLocation.Either));
    }

    [Fact]
    public void AVisitHomeNeedsAnAddress()
    {
        Assert.Equal(ServiceErrors.AddressRequired, CanRequest(Type(), location: ServiceLocation.AtDonorPlace, address: "  "));
    }

    [Fact]
    public void TheAdvanceWindowIsCountedInColomboDays()
    {
        // 23:00 Thursday in Colombo is 17:30 UTC Thursday; Friday 05:00 in Colombo is 23:30 UTC,
        // still Thursday. One Colombo day ahead, though the UTC dates are the same.
        var thursdayLateUtc = new DateTime(2026, 11, 19, 17, 30, 0, DateTimeKind.Utc);
        var fridayEarlyUtc = new DateTime(2026, 11, 19, 23, 30, 0, DateTimeKind.Utc);

        Assert.Null(CanRequest(Type(), startsAt: fridayEarlyUtc, now: thursdayLateUtc));
        Assert.Equal(ServiceErrors.TooSoon, CanRequest(Type(t => t.MinAdvanceDays = 2), startsAt: fridayEarlyUtc, now: thursdayLateUtc));
    }

    [Fact]
    public void TheSameDayIsTooSoonUnlessTheServiceAllowsIt()
    {
        var thisAfternoonUtc = ThursdayMorningUtc.AddHours(6);

        Assert.Equal(ServiceErrors.TooSoon, CanRequest(Type(), startsAt: thisAfternoonUtc));
        Assert.Null(CanRequest(Type(t => t.MinAdvanceDays = 0), startsAt: thisAfternoonUtc));
    }

    [Fact]
    public void ATimeAlreadyPastIsTooSoonEvenOnTheSameDay()
    {
        Assert.Equal(ServiceErrors.TooSoon, CanRequest(Type(t => t.MinAdvanceDays = 0), startsAt: ThursdayMorningUtc.AddMinutes(-30)));
        Assert.Equal(ServiceErrors.TooSoon, CanRequest(Type(t => t.MinAdvanceDays = 0), startsAt: ThursdayMorningUtc));
    }

    [Fact]
    public void TheLastDayOfTheWindowIsAllowedAndTheNextIsTooFar()
    {
        Assert.Null(CanRequest(Type(), startsAt: FridayTenUtc.AddDays(29)));
        Assert.Equal(ServiceErrors.TooFar, CanRequest(Type(), startsAt: FridayTenUtc.AddDays(30)));
    }

    [Fact]
    public void OnlyAVisitHomeCarriesTravelTime()
    {
        Assert.Equal(30, Type().BufferFor(ServiceLocation.AtDonorPlace));
        Assert.Equal(0, Type().BufferFor(ServiceLocation.AtInstitution));
    }
}

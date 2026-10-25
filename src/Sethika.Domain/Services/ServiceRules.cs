using Sethika.Domain.Bookings;

namespace Sethika.Domain.Services;

/// <summary>
/// Whether a donor may request a service at a time and place (Phase E, section 4, flow 1): pure,
/// like <see cref="MealSlotRules"/>, so it is unit-tested here.
/// </summary>
/// <remarks>
/// A request holds no leader, so nothing here asks whether anyone is free: that is decided when
/// leaders are assigned, by the time blocks (V2).
/// </remarks>
public static class ServiceRules
{
    /// <summary>
    /// The first reason the request is refused (a <see cref="ServiceErrors"/> code), or
    /// <see langword="null"/> if it may be made.
    /// </summary>
    /// <param name="institutionStatus">The type's institution's <c>Status</c>.</param>
    /// <param name="startsAt">UTC. Counted in Colombo dates for the advance window (<see cref="SriLankaTime"/>).</param>
    /// <param name="location"><see cref="ServiceLocation.AtInstitution"/> or <see cref="ServiceLocation.AtDonorPlace"/>.</param>
    /// <param name="address">Where, for a visit to the donor's place.</param>
    public static string? CanRequest(
        ServiceType type,
        string institutionStatus,
        DateTime startsAt,
        string location,
        string? address,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsActive)
        {
            return ServiceErrors.ServiceInactive;
        }

        if (institutionStatus != MealSlotRules.ActiveInstitution)
        {
            return ServiceErrors.InstitutionNotActive;
        }

        if (!TimeBlocks.IsAligned(startsAt))
        {
            return ServiceErrors.NotOnTheGrid;
        }

        if (!type.Offers(location))
        {
            return ServiceErrors.LocationNotOffered;
        }

        if (location == ServiceLocation.AtDonorPlace && string.IsNullOrWhiteSpace(address))
        {
            return ServiceErrors.AddressRequired;
        }

        var daysAhead = SriLankaTime.Today(startsAt).DayNumber - SriLankaTime.Today(utcNow).DayNumber;

        if (startsAt <= utcNow || daysAhead < type.MinAdvanceDays)
        {
            return ServiceErrors.TooSoon;
        }

        return daysAhead > type.MaxAdvanceDays ? ServiceErrors.TooFar : null;
    }
}

/// <summary>
/// Why a service request or a change to it was refused. Each is also the API's error code.
/// </summary>
public static class ServiceErrors
{
    // -------------------------------------------------- requesting, in the order they are checked

    public const string ServiceInactive = "service_inactive";
    public const string InstitutionNotActive = "institution_not_active";

    /// <summary>The start is not on the 15-minute grid (V11).</summary>
    public const string NotOnTheGrid = "not_on_the_grid";

    public const string LocationNotOffered = "location_not_offered";
    public const string AddressRequired = "address_required";
    public const string TooSoon = "too_soon";
    public const string TooFar = "too_far";

    // -------------------------------------------------- leaders (V2, V3)

    /// <summary>A chosen leader already has something in that time: an appointment or leave.</summary>
    public const string LeaderNotFree = "leader_not_free";

    /// <summary>Another assignment of the same leader is in progress; try again (V3).</summary>
    public const string TryAgain = "try_again";

    /// <summary>The leader is already assigned and has not declined.</summary>
    public const string AlreadyAssigned = "already_assigned";

    /// <summary>More leaders than the service needs.</summary>
    public const string TooManyLeaders = "too_many_leaders";

    /// <summary>The leader is not assigned to this appointment, or no longer.</summary>
    public const string NotAssigned = "not_assigned";

    /// <summary>The leader is inactive, or not on this institution's roster.</summary>
    public const string LeaderUnavailable = "leader_unavailable";

    /// <summary>Leave that overlaps an appointment a leader is assigned to.</summary>
    public const string TimeOffClashes = "time_off_clashes";

    // -------------------------------------------------- transitions

    public const string InvalidTransition = "invalid_transition";

    /// <summary>Only the donor who asked may cancel as a donor.</summary>
    public const string NotDonor = "not_donor";

    /// <summary>A donor may cancel only until the end of the day before (V13).</summary>
    public const string CancelWindowClosed = "cancel_window_closed";

    /// <summary>A service can be completed only once it has ended.</summary>
    public const string TooEarly = "too_early";

    /// <summary>An appointment that has ended can no longer be cancelled.</summary>
    public const string AlreadyOver = "already_over";

    public const string ReasonRequired = "reason_required";
    public const string ReasonTooLong = "reason_too_long";
}

/// <summary>The outcome of asking an appointment for a change: done, or the reason it was refused.</summary>
public readonly record struct ServiceResult(string? Error)
{
    public bool Succeeded => Error is null;

    public static ServiceResult Ok { get; }

    public static ServiceResult Fail(string error) => new(error);
}

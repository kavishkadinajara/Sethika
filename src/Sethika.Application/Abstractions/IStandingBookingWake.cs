namespace Sethika.Application.Abstractions;

/// <summary>
/// Starts the standing job now rather than at night (plan 08, K3): a new sponsorship whose window holds
/// more days than are booked while staff wait has the rest booked within moments.
/// </summary>
/// <remarks>The nightly run is still the guarantee: a wake that fails leaves the days to it, never lost.</remarks>
public interface IStandingBookingWake
{
    void Wake();
}

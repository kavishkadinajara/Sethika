using Sethika.Application.Bookings.Sponsors;

namespace Sethika.Application.Abstractions;

/// <summary>
/// A linked standing sponsor's own "I can't give that day" from My bookings (plan 08, K5, K24, K35).
/// </summary>
/// <remarks>
/// The sponsor's session reads their standing days only as their own bookings and cannot write
/// <c>booking.StandingExceptions</c> (K16), so this one change runs with the platform's reach. Its only
/// input is the caller's id and a booking number, and it acts only on the sponsorship whose donor is
/// linked to the caller and whose booking is the caller's: the staff skip's own rules decide the rest
/// (<see cref="StandingSponsorshipHandler.SkipOwnAsync"/>).
/// </remarks>
public interface IOwnStandingDays
{
    /// <summary>Lets go the day of the caller's standing booking <paramref name="bookingNo"/>.</summary>
    Task<SponsorshipChangeResult> SkipAsync(Guid userId, string bookingNo, string? reason, CancellationToken cancellationToken = default);
}

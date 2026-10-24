using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>The limits on the donor's side of booking (plan 03, 5.5).</summary>
internal static class BookingRateLimits
{
    /// <summary>
    /// The anonymous institution reads from one address: the list, a page, a month of availability.
    /// Loose, like the refresh limit, because a temple's donors may share one carrier address and
    /// each month a donor pages through is a request. It bounds scraping and load, nothing secret.
    /// </summary>
    public static readonly RateLimitRule PublicReadPerClient = new("institution-read-client", 600, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Bookings made by one account. Each takes a number from the platform-wide series under its row
    /// lock (§8, E11) and will send an SMS (5.6), so booking and cancelling in a loop is bounded here.
    /// </summary>
    public static readonly RateLimitRule CreatePerUser = new("booking-create-user", 20, TimeSpan.FromHours(1));
}

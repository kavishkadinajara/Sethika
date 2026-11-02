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

    /// <summary>
    /// Service requests made by one account (Phase E). The same reasons as a booking: a number from a
    /// platform-wide series, and the institution's attention. A household asks for a few a year.
    /// </summary>
    public static readonly RateLimitRule RequestServicePerUser = new("appointment-create-user", 10, TimeSpan.FromHours(1));

    /// <summary>
    /// Memorial plans made by one account (plan 07, H6). A family makes a handful; the limit keeps a
    /// loop from filling the table and the reminder job's nightly read.
    /// </summary>
    public static readonly RateLimitRule MemorialPlanPerUser = new("memorial-create-user", 10, TimeSpan.FromHours(1));

    /// <summary>
    /// Invitations to share a booking made by one account (plan 07, H16). A booking holds ten at most;
    /// this bounds a loop of inviting and withdrawing across bookings.
    /// </summary>
    public static readonly RateLimitRule InvitePerUser = new("share-invite-user", 30, TimeSpan.FromHours(1));

    /// <summary>
    /// Co-sponsor links opened from one address (H17). A token is 256 random bits, so guessing is no
    /// threat; the limit bounds load from a script, while a family opening its links never meets it.
    /// </summary>
    public static readonly RateLimitRule ShareReadPerClient = new("share-read-client", 120, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Places kept by one account (plan 07, H12). A heart is tapped now and then; the limit bounds a
    /// loop filling the table, since a place is kept without a cap.
    /// </summary>
    public static readonly RateLimitRule PlacePerUser = new("place-keep-user", 60, TimeSpan.FromHours(1));

    /// <summary>Joins and declines by one account (H17): a person answers a few links, not dozens.</summary>
    public static readonly RateLimitRule ShareAnswerPerUser = new("share-answer-user", 30, TimeSpan.FromHours(1));
}

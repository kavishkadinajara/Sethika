using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Notifications;

/// <summary>One reminder run: the day it covered, the bookings on it, and how many were texted.</summary>
/// <param name="Sent">Donors and co-sponsors texted.</param>
/// <param name="NoPhone">Donors and co-sponsors with no confirmed phone; nothing was sent to them.</param>
/// <param name="Contributors">The co-sponsors of those bookings (plan 07, H19).</param>
public sealed record ReminderRun(DateOnly Date, int Bookings, int Sent, int NoPhone, int Contributors = 0);

/// <summary>
/// The evening reminder (plan 03, C11, Q3): every approved booking for tomorrow, Colombo time, texted
/// to its donor once, and to each person who joined it as a co-sponsor once (plan 07, H19).
/// </summary>
/// <remarks>
/// Only <c>Approved</c> bookings: a request the institution never answered is not a meal to remind
/// anyone of. The dedup key <c>booking-reminder:{BookingNo}</c> makes a run safe to repeat - a second
/// run, a retry after a failure, or a run on another instance texts nobody twice. A failed send stops
/// the run, as a failed message stops the outbox; the retry carries on from where it stopped, because
/// everything before it is already sent under its key.
/// </remarks>
public sealed class BookingReminders(IBookingNotificationQueries queries, IUserTextMessages texts, TimeProvider timeProvider)
{
    public const string Template = "BOOKING_REMINDER";

    /// <summary>The key a booking's reminder is sent under, at most once.</summary>
    public static string DedupKey(string bookingNo) => $"booking-reminder:{bookingNo}";

    /// <summary>The key a co-sponsor's reminder of a booking is sent under, at most once each.</summary>
    public static string DedupKey(string bookingNo, Guid contributorUserId) => $"booking-reminder:{bookingNo}:{contributorUserId}";

    public async Task<ReminderRun> SendForTomorrowAsync(CancellationToken cancellationToken = default)
    {
        var tomorrow = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        var bookings = await queries.ListApprovedOnAsync(tomorrow, cancellationToken).ConfigureAwait(false);
        var contributors = await queries.ListJoinedOnAsync(tomorrow, cancellationToken).ConfigureAwait(false);

        var sent = 0;
        var noPhone = 0;
        var recipients = bookings.Select(b => (Text: b, Key: DedupKey(b.BookingNo)))
            .Concat(contributors.Select(c => (Text: c, Key: DedupKey(c.BookingNo, c.DonorUserId!.Value))));
        foreach (var (booking, key) in recipients)
        {
            var outcome = await texts
                .SendAsync(booking.DonorUserId!.Value, Template, BookingTexts.Values(booking), key, cancellationToken)
                .ConfigureAwait(false);

            if (outcome == UserTextOutcome.Sent)
            {
                sent++;
            }
            else
            {
                noPhone++;
            }
        }

        return new ReminderRun(tomorrow, bookings.Count, sent, noPhone, contributors.Count);
    }
}

using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sponsors;

/// <summary>One of a sponsorship's next days and where it stands.</summary>
/// <param name="State">
/// <c>booked</c> (see <paramref name="BookingStatus"/>); <c>cancelled</c> on the calendar; an exception's
/// kind (<c>Skipped</c>, <c>Released</c>, <c>Blocked</c>, <c>MovedTo</c>, <c>MovedFrom</c>); <c>open</c>,
/// in the booking window but not booked (the engine refused it; the nightly job tries again); or
/// <c>upcoming</c>, beyond the window, booked when the window reaches it.
/// </param>
/// <param name="Reason">An exception's reason.</param>
public sealed record SponsorshipDay(DateOnly Date, string State, string? BookingNo, string? BookingStatus, string? Reason);

/// <summary>A sponsorship and its next days (K2, K9).</summary>
public sealed record SponsorshipDetails(SponsorshipView Sponsorship, IReadOnlyList<SponsorshipDay> Days);

/// <summary>
/// Reads one sponsorship and lays its next days out from the pattern (plan 08, K2): the same
/// arithmetic the nightly job books by, so the screen shows the days the job will book.
/// </summary>
public sealed class StandingSponsorshipReader(IStandingSponsorshipQueries queries, TimeProvider timeProvider)
{
    /// <summary>How many of the pattern's days are shown.</summary>
    public const int NextDayCount = 12;

    public const string Booked = "booked";
    public const string Cancelled = "cancelled";
    public const string Open = "open";
    public const string Upcoming = "upcoming";

    public async Task<SponsorshipDetails?> GetAsync(int institutionId, Guid publicId, CancellationToken cancellationToken = default)
    {
        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        var record = await queries.GetAsync(institutionId, publicId, today, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var view = record.Sponsorship;
        var pattern = new StandingSponsorship
        {
            Pattern = view.Pattern,
            MonthInterval = (byte)view.MonthInterval,
            WeekDays = StandingPattern.WeekMask(view.WeekDays),
            MonthDays = StandingPattern.MonthMask(view.MonthDays),
            YearMonth = (byte?)view.YearMonth,
            YearDay = (byte?)view.YearDay,
            StartDate = view.StartDate,
            EndDate = view.EndDate,
        };

        var windowEnd = today.AddDays(view.MaxAdvanceDays);
        var exceptions = record.Exceptions.ToDictionary(e => e.Date);

        // The last booking of a day speaks for it.
        var bookings = record.Bookings.GroupBy(b => b.Date).ToDictionary(g => g.Key, g => g.Last());

        var days = pattern.NextDays(today, NextDayCount)
            .Select(date =>
            {
                if (exceptions.TryGetValue(date, out var exception))
                {
                    return new SponsorshipDay(date, exception.Kind, bookings.GetValueOrDefault(date)?.BookingNo, bookings.GetValueOrDefault(date)?.Status, exception.Reason);
                }

                if (bookings.TryGetValue(date, out var booking))
                {
                    var state = BookingStatus.HoldsPlace(booking.Status) ? Booked : Cancelled;
                    return new SponsorshipDay(date, state, booking.BookingNo, booking.Status, null);
                }

                return new SponsorshipDay(date, date <= windowEnd ? Open : Upcoming, null, null, null);
            })
            .ToList();

        return new SponsorshipDetails(view, days);
    }
}

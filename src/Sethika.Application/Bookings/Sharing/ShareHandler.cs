using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Sharing;

/// <summary>
/// What a co-sponsor's link shows anyone who opens it (plan 07, H17): the booking's public summary and
/// nothing else - no booking number, phone, headcount or notes - with the invitation's and the
/// booking's state.
/// </summary>
/// <param name="Institution">The institution's slug.</param>
/// <param name="Invitation">A <see cref="ContributorStatus"/> value.</param>
/// <param name="Booking">A <see cref="ShareBookingState"/> value.</param>
public sealed record ShareSummary(
    string Institution,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string MealName,
    string? MealNameSi,
    string? MealNameTa,
    DateOnly Date,
    string StartTime,
    string DonorName,
    string? InMemoryOf,
    string Invitation,
    string Booking);

/// <summary>Where the shared booking stands, as its link shows it.</summary>
public static class ShareBookingState
{
    /// <summary>Requested or approved, today or later: it can be joined.</summary>
    public const string Open = "Open";

    public const string Cancelled = "Cancelled";
    public const string Rejected = "Rejected";

    /// <summary>Its day has gone, or it was served or missed.</summary>
    public const string Past = "Past";

    /// <summary>The state of a booking in <paramref name="status"/> on <paramref name="date"/>, on <paramref name="today"/>.</summary>
    public static string Of(string status, DateOnly date, DateOnly today) => status switch
    {
        _ when BookingContributor.IsShareable(status, date, today) => Open,
        BookingStatus.Cancelled => Cancelled,
        BookingStatus.Rejected => Rejected,
        _ => Past,
    };
}

/// <summary>The refusals of an answer to a co-sponsor's link.</summary>
public static class ShareErrors
{
    /// <summary>No invitation under that link: never made, mistyped, or withdrawn.</summary>
    public const string LinkNotFound = "link_not_found";

    /// <summary>The donor opened their own link.</summary>
    public const string OwnBooking = "own_booking";

    /// <summary>The booking is cancelled, rejected or past.</summary>
    public const string BookingClosed = "booking_closed";

    /// <summary>The invitation was joined or declined already.</summary>
    public const string InvitationAnswered = "invitation_answered";

    /// <summary>The person already joined this booking through another link.</summary>
    public const string AlreadyJoined = "already_joined";
}

/// <summary>Opening, joining and declining a co-sponsor's link, and "Shared with me" (plan 07, H17-H19).</summary>
/// <remarks>
/// The person is on neither the invitation nor the booking until they join, so these go through
/// <see cref="IShareLinks"/>, which runs as the platform but only ever by the link's hash or the
/// person's own id (H18).
/// </remarks>
public sealed class ShareHandler(IShareLinks links, TimeProvider timeProvider)
{
    /// <summary>The summary behind <paramref name="token"/>, or <see langword="null"/> if it opens nothing.</summary>
    public async Task<ShareSummary?> OpenAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (ShareToken.HashOf(token) is not { } hash
            || await links.FindAsync(hash, cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return null;
        }

        var today = SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime);
        return new ShareSummary(
            row.InstitutionSlug, row.InstitutionName, row.InstitutionNameSi, row.InstitutionNameTa,
            row.MealName, row.MealNameSi, row.MealNameTa, row.Date, row.StartTime, row.DonorName, row.InMemoryOf,
            row.Status, ShareBookingState.Of(row.BookingStatus, row.Date, today));
    }

    /// <summary>
    /// <paramref name="userId"/> joins (<paramref name="join"/>) or declines the invitation behind
    /// <paramref name="token"/>. <see langword="null"/> on success, else one of <see cref="ShareErrors"/>.
    /// </summary>
    public async Task<string?> AnswerAsync(string? token, Guid userId, bool join, CancellationToken cancellationToken = default)
    {
        if (ShareToken.HashOf(token) is not { } hash
            || await links.FindAsync(hash, cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return ShareErrors.LinkNotFound;
        }

        if (row.InvitedBy == userId)
        {
            return ShareErrors.OwnBooking;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!BookingContributor.IsShareable(row.BookingStatus, row.Date, SriLankaTime.Today(now)))
        {
            return ShareErrors.BookingClosed;
        }

        if (row.Status != ContributorStatus.Invited)
        {
            return ShareErrors.InvitationAnswered;
        }

        return await links.AnswerAsync(row.ContributorId, userId, join, now, cancellationToken).ConfigureAwait(false) switch
        {
            ShareAnswerOutcome.Answered => null,
            ShareAnswerOutcome.AlreadyJoined => ShareErrors.AlreadyJoined,
            _ => ShareErrors.InvitationAnswered,
        };
    }

    /// <summary>The bookings <paramref name="userId"/> joined (H19).</summary>
    public Task<IReadOnlyList<SharedBookingView>> ListJoinedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        links.ListJoinedAsync(userId, cancellationToken);
}

namespace Sethika.Application.Abstractions;

/// <summary>
/// What a co-sponsor's link opens on (plan 07, H17): the booking's public summary, and the state of
/// the invitation and the booking. Internal ids stay on the server, but for <see cref="ContributorId"/>,
/// which the handler hands straight back to <see cref="IShareLinks.AnswerAsync"/>.
/// </summary>
/// <param name="ContributorId"><c>booking.BookingContributors.Id</c>; never sent to a client.</param>
/// <param name="InvitedBy">The booking's donor, who may not answer their own invitation.</param>
/// <param name="Status">The invitation's <c>ContributorStatus</c>.</param>
/// <param name="BookingStatus">The booking's <c>BookingStatus</c>.</param>
/// <param name="DonorName">The name the donor books under: their display name, else their name.</param>
public sealed record ShareLinkRow(
    long ContributorId,
    Guid InvitedBy,
    string Status,
    string BookingStatus,
    string InstitutionSlug,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string MealName,
    string? MealNameSi,
    string? MealNameTa,
    DateOnly Date,
    string StartTime,
    string DonorName,
    string? InMemoryOf);

/// <summary>A booking the signed-in person joined as a co-sponsor (plan 07, H19), as "Shared with me" lists it.</summary>
/// <param name="Status">The booking's <c>BookingStatus</c>: a cancelled or rejected one shows as such.</param>
public sealed record SharedBookingView(
    string BookingNo,
    string Status,
    string InstitutionSlug,
    string InstitutionName,
    string? InstitutionNameSi,
    string? InstitutionNameTa,
    string MealName,
    string? MealNameSi,
    string? MealNameTa,
    DateOnly Date,
    string StartTime,
    short Headcount,
    string DonorName,
    string? InMemoryOf,
    DateTime JoinedAt);

/// <summary>What became of an answer to an invitation.</summary>
public enum ShareAnswerOutcome
{
    /// <summary>Joined or declined, as asked.</summary>
    Answered,

    /// <summary>Someone answered it first: it is no longer <c>Invited</c>.</summary>
    AlreadyAnswered,

    /// <summary>The person had already joined this booking through another of its links.</summary>
    AlreadyJoined,
}

/// <summary>
/// The co-sponsor reads and the answer that cannot run in the person's own session (plan 07, H18).
/// </summary>
/// <remarks>
/// The person opening a link is not yet on the invitation, and a contributor is not on the booking,
/// so Row-Level Security would show them neither. These run as the platform instead, and each is
/// narrowed by what the caller holds: a link's hash, or the signed-in person's own id. Nothing else
/// may reach this interface.
/// </remarks>
public interface IShareLinks
{
    /// <summary>The invitation stored under <paramref name="tokenHash"/>, or <see langword="null"/>.</summary>
    Task<ShareLinkRow?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers invitation <paramref name="contributorId"/> for <paramref name="userId"/> if it is still
    /// <c>Invited</c>: joined (<paramref name="join"/>) or declined, at <paramref name="now"/>.
    /// </summary>
    Task<ShareAnswerOutcome> AnswerAsync(long contributorId, Guid userId, bool join, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The bookings <paramref name="userId"/> joined, latest slot-day first, at most <see cref="MaxJoined"/>.</summary>
    Task<IReadOnlyList<SharedBookingView>> ListJoinedAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The most bookings <see cref="ListJoinedAsync"/> returns, as My bookings' own list.</summary>
    const int MaxJoined = 200;
}

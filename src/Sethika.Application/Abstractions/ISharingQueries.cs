namespace Sethika.Application.Abstractions;

/// <summary>One invitation to share a booking, as its donor sees it (plan 07, H16).</summary>
/// <param name="PublicId">How the donor withdraws it.</param>
/// <param name="Status">A <c>ContributorStatus</c> value.</param>
public sealed record InvitationView(
    Guid PublicId,
    string Name,
    string? Note,
    string Status,
    DateTime InvitedAt,
    DateTime? JoinedAt,
    DateTime? DeclinedAt);

/// <summary>Someone who joined a booking, as its institution sees them (plan 07, H19): a name only.</summary>
public sealed record SharedWithView(string Name, DateTime JoinedAt);

/// <summary>
/// Dapper reads of co-sponsors in the caller's own session (plan 07, H18): Row-Level Security shows
/// the donor the invitations they made, and the institution those on its bookings.
/// </summary>
public interface ISharingQueries
{
    /// <summary>
    /// The invitations on <paramref name="donorId"/>'s booking <paramref name="bookingNo"/>, in the order
    /// they were made; <see langword="null"/> when the booking is not theirs.
    /// </summary>
    Task<IReadOnlyList<InvitationView>?> ListInvitationsAsync(Guid donorId, string bookingNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Who joined institution <paramref name="institutionId"/>'s booking <paramref name="bookingNo"/>;
    /// <see langword="null"/> when it has no such booking.
    /// </summary>
    Task<IReadOnlyList<SharedWithView>?> ListSharedWithAsync(int institutionId, string bookingNo, CancellationToken cancellationToken = default);
}

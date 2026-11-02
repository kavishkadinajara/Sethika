using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads of co-sponsors in the caller's own session (plan 07, H18).</summary>
/// <remarks>
/// Row-Level Security (<c>sec.fn_Contributor</c>) decides who may see an invitation; each query also
/// names the donor or the institution, as <see cref="DonorBookingQueries"/> does, so it answers for
/// this screen's booking and nothing RLS happens to show besides.
/// </remarks>
public sealed class SharingQueries(IDbConnectionFactory connections) : ISharingQueries
{
    /// <summary><c>booking.Bookings.BookingNo</c> is <c>VARCHAR(20)</c>; sent as such, its unique index is seeked.</summary>
    private const int BookingNoLength = 20;

    private const string InvitationsSql =
        """
        SELECT COUNT(*) FROM booking.Bookings AS b WHERE b.BookingNo = @bookingNo AND b.DonorUserId = @donorId;

        SELECT c.PublicId AS PublicId, c.Name AS Name, c.ContributionNote AS Note, c.Status AS Status,
               c.InvitedAt AS InvitedAt, c.JoinedAt AS JoinedAt, c.DeclinedAt AS DeclinedAt
        FROM booking.BookingContributors AS c
        JOIN booking.Bookings AS b ON b.Id = c.BookingId AND b.InstitutionId = c.InstitutionId
        WHERE b.BookingNo = @bookingNo AND b.DonorUserId = @donorId AND c.InvitedBy = @donorId
        ORDER BY c.InvitedAt, c.Id;
        """;

    private const string SharedWithSql =
        """
        SELECT COUNT(*) FROM booking.Bookings AS b WHERE b.BookingNo = @bookingNo AND b.InstitutionId = @institutionId;

        SELECT c.Name AS Name, c.JoinedAt AS JoinedAt
        FROM booking.BookingContributors AS c
        JOIN booking.Bookings AS b ON b.Id = c.BookingId AND b.InstitutionId = c.InstitutionId
        WHERE b.BookingNo = @bookingNo AND b.InstitutionId = @institutionId AND c.Status = 'Joined'
        ORDER BY c.JoinedAt, c.Id;
        """;

    public async Task<IReadOnlyList<InvitationView>?> ListInvitationsAsync(Guid donorId, string bookingNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            InvitationsSql,
            new { donorId, bookingNo = BookingNo(bookingNo) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (await results.ReadSingleAsync<int>().ConfigureAwait(false) == 0)
        {
            return null;
        }

        return [.. await results.ReadAsync<InvitationView>().ConfigureAwait(false)];
    }

    public async Task<IReadOnlyList<SharedWithView>?> ListSharedWithAsync(int institutionId, string bookingNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            SharedWithSql,
            new { institutionId, bookingNo = BookingNo(bookingNo) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (await results.ReadSingleAsync<int>().ConfigureAwait(false) == 0)
        {
            return null;
        }

        return [.. await results.ReadAsync<SharedWithView>().ConfigureAwait(false)];
    }

    private static DbString BookingNo(string bookingNo) => new() { Value = bookingNo.Trim(), IsAnsi = true, Length = BookingNoLength };
}

using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper counts for the sidebar's badges (plan 05, R3), per the reads-use-Dapper rule.</summary>
/// <remarks>
/// The connection carries the caller's session context, so RLS scopes the booking count to their
/// institution as well as the <c>InstitutionId</c> filter does; the platform's count is only asked
/// for by a caller holding <c>ONBOARDING.REQUESTS.VIEW</c>.
/// </remarks>
public sealed class ShellQueries(IDbConnectionFactory connections) : IShellQueries
{
    // The same statuses the review queue lists by default (RegistrationReviewQueries.Open).
    private static readonly string[] Waiting = ["Submitted", "UnderReview", "InfoRequested"];

    private const string BookingsToAnswerSql =
        """
        SELECT COUNT(*)
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        WHERE b.InstitutionId = @institutionId
          AND b.Status = 'Requested'
          AND si.SlotDate >= @today;
        """;

    private const string RegistrationsWaitingSql =
        """
        SELECT COUNT(*)
        FROM org.RegistrationRequests AS r
        WHERE r.Status IN @waiting;
        """;

    public async Task<int> CountBookingsToAnswerAsync(int institutionId, DateOnly today, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            BookingsToAnswerSql,
            new { institutionId, today },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> CountRegistrationsWaitingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            RegistrationsWaitingSql,
            new { waiting = Waiting },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}

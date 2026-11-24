using Dapper;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Queries;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IStandingHolds"/>: counts of standing bookings per slot-day, read as the platform (plan 08, K25).
/// </summary>
/// <remarks>
/// A donor's session sees only their own bookings, and the public calendar has no session at all, yet
/// both need to know that a full day is full through a regular donor's hold. Like <c>SqlShareLinks</c>
/// (H33), its connections are stamped as the platform here, never from the request's user, and the one
/// statement returns counts keyed by the institution asked about: no name, phone or booking number.
/// </remarks>
public sealed class SqlStandingHolds(string connectionString) : IStandingHolds
{
    private const string HoldsSql =
        """
        SELECT si.MealSlotId AS MealSlotId, si.SlotDate AS Date, COUNT(*) AS Sponsors, MAX(b.Headcount) AS Headcount
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        WHERE b.InstitutionId = @institutionId AND b.Source = 'Standing' AND b.Status IN ('Requested', 'Approved')
          AND si.SlotDate >= @first AND si.SlotDate <= @last
        GROUP BY si.MealSlotId, si.SlotDate;
        """;

    private readonly SqlConnectionFactory _platform = new(connectionString, PlatformJobUser.Instance);

    public async Task<IReadOnlyList<StandingHold>> ListAsync(int institutionId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            HoldsSql,
            new { institutionId, first, last },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new StandingHold(r.MealSlotId, r.Date, r.Sponsors, r.Headcount))];
    }

    // A row class rather than the record: Dapper's constructor matching does not apply the DateOnly handler.
    private sealed class Row
    {
        public int MealSlotId { get; init; }
        public DateOnly Date { get; init; }
        public int Sponsors { get; init; }
        public short Headcount { get; init; }
    }
}

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Queries;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IShareLinks"/>: the co-sponsor reads and answer that run as the platform (plan 07, H18).
/// </summary>
/// <remarks>
/// <para>
/// The one place a request reaches past Row-Level Security, and so the narrowest: each statement is
/// keyed by what the caller holds - the SHA-256 of a link's secret, the invitation that hash found, or
/// the signed-in person's own id - and returns only the summary fields H17 lists. Its connections are
/// stamped as the platform here, never from the request's user, so nothing else in the request
/// gains the platform's reach.
/// </para>
/// <para>
/// The answer is one conditional UPDATE: it takes the invitation only while it is still <c>Invited</c>,
/// so two people answering at once cannot both have it; and <c>UX_Contributors_JoinedOnce</c> refuses
/// a second joining of one booking by one person, whichever link they used.
/// </para>
/// </remarks>
public sealed class SqlShareLinks(string connectionString) : IShareLinks
{
    private const string JoinedOnceIndex = "UX_Contributors_JoinedOnce";
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;

    private const string Joins =
        """
        FROM booking.BookingContributors AS c
        JOIN booking.Bookings AS b ON b.Id = c.BookingId AND b.InstitutionId = c.InstitutionId
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
        JOIN org.Institutions AS i ON i.Id = b.InstitutionId
        """;

    private const string Summary =
        """
        b.Status AS BookingStatus, i.Slug AS InstitutionSlug, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
        i.NameTa AS InstitutionNameTa, s.Name AS MealName, s.NameSi AS MealNameSi, s.NameTa AS MealNameTa,
        si.SlotDate AS Date, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime,
        COALESCE(b.SponsorDisplayName, b.DonorName) AS DonorName, b.InMemoryOf AS InMemoryOf
        """;

    private const string FindSql =
        "SELECT c.Id AS ContributorId, c.InvitedBy AS InvitedBy, c.Status AS Status,\n" + Summary + "\n" + Joins +
        "\nWHERE c.InviteTokenHash = @tokenHash;";

    private const string AnswerSql =
        """
        UPDATE booking.BookingContributors
        SET UserId = @userId, Status = @status, JoinedAt = @joinedAt, DeclinedAt = @declinedAt
        WHERE Id = @contributorId AND Status = 'Invited';
        """;

    private const string JoinedSql =
        "SELECT TOP (@take) b.BookingNo AS BookingNo, b.Headcount AS Headcount, c.JoinedAt AS JoinedAt,\n" + Summary + "\n" + Joins +
        "\nWHERE c.UserId = @userId AND c.Status = 'Joined'\nORDER BY si.SlotDate DESC, s.StartTime DESC, b.Id DESC;";

    private readonly SqlConnectionFactory _platform = new(connectionString, PlatformJobUser.Instance);

    public async Task<ShareLinkRow?> FindAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            FindSql,
            new { tokenHash = new DbString { Value = tokenHash, IsAnsi = true, IsFixedLength = true, Length = 64 } },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new ShareLinkRow(
                row.ContributorId, row.InvitedBy, row.Status, row.BookingStatus, row.InstitutionSlug, row.InstitutionName,
                row.InstitutionNameSi, row.InstitutionNameTa, row.MealName, row.MealNameSi, row.MealNameTa, row.Date,
                row.StartTime, row.DonorName, row.InMemoryOf);
    }

    public async Task<ShareAnswerOutcome> AnswerAsync(long contributorId, Guid userId, bool join, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var answered = await connection.ExecuteAsync(new CommandDefinition(
                AnswerSql,
                new
                {
                    contributorId,
                    userId,
                    status = new DbString { Value = join ? "Joined" : "Declined", IsAnsi = true, Length = 10 },
                    joinedAt = join ? now : (DateTime?)null,
                    declinedAt = join ? (DateTime?)null : now,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return answered == 1 ? ShareAnswerOutcome.Answered : ShareAnswerOutcome.AlreadyAnswered;
        }
        catch (SqlException ex) when (ex.Number is DuplicateKey or UniqueConstraint && ex.Message.Contains(JoinedOnceIndex, StringComparison.Ordinal))
        {
            return ShareAnswerOutcome.AlreadyJoined;
        }
    }

    public async Task<IReadOnlyList<SharedBookingView>> ListJoinedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _platform.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            JoinedSql,
            new { userId, take = IShareLinks.MaxJoined },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => new SharedBookingView(
            r.BookingNo!, r.BookingStatus, r.InstitutionSlug, r.InstitutionName, r.InstitutionNameSi, r.InstitutionNameTa,
            r.MealName, r.MealNameSi, r.MealNameTa, r.Date, r.StartTime, r.Headcount, r.DonorName, r.InMemoryOf, r.JoinedAt!.Value))];
    }

    // A row class rather than the records: Dapper's constructor matching does not apply the DateOnly handler.
    private sealed class Row
    {
        public long ContributorId { get; init; }
        public Guid InvitedBy { get; init; }
        public string Status { get; init; } = null!;
        public string? BookingNo { get; init; }
        public short Headcount { get; init; }
        public DateTime? JoinedAt { get; init; }
        public string BookingStatus { get; init; } = null!;
        public string InstitutionSlug { get; init; } = null!;
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public string MealName { get; init; } = null!;
        public string? MealNameSi { get; init; }
        public string? MealNameTa { get; init; }
        public DateOnly Date { get; init; }
        public string StartTime { get; init; } = null!;
        public string DonorName { get; init; } = null!;
        public string? InMemoryOf { get; init; }
    }
}

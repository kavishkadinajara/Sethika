using Dapper;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Queries;

/// <summary>Dapper reads for the booking texts (plan 03, 5.6), run as the platform.</summary>
public sealed class BookingNotificationQueries(IDbConnectionFactory connections) : IBookingNotificationQueries
{
    private const string Select =
        """
        SELECT b.BookingNo AS BookingNo, b.Status AS Status, b.DonorUserId AS DonorUserId,
               u.PreferredLanguage AS Language, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, si.SlotDate AS Date, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime
        FROM booking.Bookings AS b
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
        JOIN org.Institutions AS i ON i.Id = b.InstitutionId
        LEFT JOIN auth.Users AS u ON u.Id = b.DonorUserId
        """;

    /// <summary><c>booking.Bookings.BookingNo</c> is <c>VARCHAR(20)</c>; sent as such, its unique index is seeked.</summary>
    private const int BookingNoLength = 20;

    private const string OneSql = Select + "\nWHERE b.BookingNo = @bookingNo;";

    private const string ApprovedOnSql =
        Select + "\nWHERE si.SlotDate = @slotDate AND b.Status = 'Approved' AND b.DonorUserId IS NOT NULL\nORDER BY b.Id;";

    public async Task<BookingTextDetails?> GetAsync(string bookingNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            OneSql,
            new { bookingNo = new DbString { Value = bookingNo, IsAnsi = true, Length = BookingNoLength } },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDetails();
    }

    public async Task<IReadOnlyList<BookingTextDetails>> ListApprovedOnAsync(DateOnly slotDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            ApprovedOnSql,
            new { slotDate },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDetails())];
    }

    // A row class rather than the record: Dapper's constructor matching does not apply the DateOnly handler.
    private sealed class Row
    {
        public string BookingNo { get; init; } = null!;
        public string Status { get; init; } = null!;
        public Guid? DonorUserId { get; init; }
        public string? Language { get; init; }
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public DateOnly Date { get; init; }
        public string StartTime { get; init; } = null!;

        public BookingTextDetails ToDetails() =>
            new(BookingNo, Status, DonorUserId, Language, InstitutionName, InstitutionNameSi, InstitutionNameTa, Date, StartTime);
    }
}

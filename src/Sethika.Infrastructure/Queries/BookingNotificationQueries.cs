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

    // A yearly booking's text (plan 07, H4): there is no booking, so the day comes from the outbox message.
    private const string RecurringSql =
        """
        SELECT r.DonorUserId AS DonorUserId, u.PreferredLanguage AS Language, i.Name AS InstitutionName,
               i.NameSi AS InstitutionNameSi, i.NameTa AS InstitutionNameTa, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime
        FROM booking.RecurringBookings AS r
        JOIN booking.MealSlots AS s ON s.Id = r.MealSlotId AND s.InstitutionId = r.InstitutionId
        JOIN org.Institutions AS i ON i.Id = r.InstitutionId
        JOIN auth.Users AS u ON u.Id = r.DonorUserId
        WHERE r.PublicId = @publicId;
        """;

    // Co-sponsors of approved bookings (plan 07, H19): the same text, to the account that joined.
    private const string JoinedOnSql =
        """
        SELECT b.BookingNo AS BookingNo, b.Status AS Status, c.UserId AS DonorUserId,
               u.PreferredLanguage AS Language, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, si.SlotDate AS Date, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime
        FROM booking.BookingContributors AS c
        JOIN booking.Bookings AS b ON b.Id = c.BookingId AND b.InstitutionId = c.InstitutionId
        JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
        JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
        JOIN org.Institutions AS i ON i.Id = b.InstitutionId
        JOIN auth.Users AS u ON u.Id = c.UserId
        WHERE si.SlotDate = @slotDate AND b.Status = 'Approved' AND c.Status = 'Joined'
        ORDER BY c.JoinedAt, c.Id;
        """;

    // A standing sponsorship's texts (plan 08, K10): its donor, and the account the donor book links.
    private const string StandingSql =
        """
        SELECT d.UserId AS DonorUserId, d.Phone AS Phone, d.Language AS DonorLanguage,
               CAST(CASE WHEN d.TextConsentAt IS NULL THEN 0 ELSE 1 END AS BIT) AS TextConsent,
               u.PreferredLanguage AS AccountLanguage, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime
        FROM booking.StandingSponsorships AS ss
        JOIN org.InstitutionDonors AS d ON d.Id = ss.InstitutionDonorId AND d.InstitutionId = ss.InstitutionId
        JOIN booking.MealSlots AS s ON s.Id = ss.MealSlotId AND s.InstitutionId = ss.InstitutionId
        JOIN org.Institutions AS i ON i.Id = ss.InstitutionId
        LEFT JOIN auth.Users AS u ON u.Id = d.UserId
        WHERE ss.PublicId = @publicId;
        """;

    // A day request's answer (plan 08, K10), to the donor who asked.
    private const string DayRequestSql =
        """
        SELECT r.RequestNo AS RequestNo, b.BookingNo AS BookingNo, r.DonorUserId AS DonorUserId,
               u.PreferredLanguage AS Language, i.Name AS InstitutionName, i.NameSi AS InstitutionNameSi,
               i.NameTa AS InstitutionNameTa, r.RequestedDate AS Date, CONVERT(CHAR(5), s.StartTime, 108) AS StartTime
        FROM booking.DayRequests AS r
        JOIN booking.MealSlots AS s ON s.Id = r.MealSlotId AND s.InstitutionId = r.InstitutionId
        JOIN org.Institutions AS i ON i.Id = r.InstitutionId
        JOIN auth.Users AS u ON u.Id = r.DonorUserId
        LEFT JOIN booking.Bookings AS b ON b.Id = r.BookingId AND b.InstitutionId = r.InstitutionId
        WHERE r.RequestNo = @requestNo;
        """;

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

    public async Task<BookingTextDetails?> GetRecurringAsync(Guid publicId, DateOnly slotDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<RecurringRow>(new CommandDefinition(
            RecurringSql,
            new { publicId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new BookingTextDetails(string.Empty, string.Empty, row.DonorUserId, row.Language, row.InstitutionName,
                row.InstitutionNameSi, row.InstitutionNameTa, slotDate, row.StartTime);
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

    public async Task<IReadOnlyList<BookingTextDetails>> ListJoinedOnAsync(DateOnly slotDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            JoinedOnSql,
            new { slotDate },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDetails())];
    }

    public async Task<StandingTextDetails?> GetStandingAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<StandingRow>(new CommandDefinition(
            StandingSql,
            new { publicId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new StandingTextDetails(row.DonorUserId, row.Phone, row.DonorLanguage, row.TextConsent, row.AccountLanguage,
                row.InstitutionName, row.InstitutionNameSi, row.InstitutionNameTa, row.StartTime);
    }

    public async Task<DayRequestTextDetails?> GetDayRequestAsync(string requestNo, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<DayRequestRow>(new CommandDefinition(
            DayRequestSql,
            new { requestNo = new DbString { Value = requestNo, IsAnsi = true, Length = BookingNoLength } },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new DayRequestTextDetails(row.RequestNo, row.BookingNo, row.DonorUserId, row.Language, row.InstitutionName,
                row.InstitutionNameSi, row.InstitutionNameTa, row.Date, row.StartTime);
    }

    private sealed class StandingRow
    {
        public Guid? DonorUserId { get; init; }
        public string Phone { get; init; } = null!;
        public string DonorLanguage { get; init; } = null!;
        public bool TextConsent { get; init; }
        public string? AccountLanguage { get; init; }
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public string StartTime { get; init; } = null!;
    }

    private sealed class DayRequestRow
    {
        public string RequestNo { get; init; } = null!;
        public string? BookingNo { get; init; }
        public Guid DonorUserId { get; init; }
        public string? Language { get; init; }
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public DateOnly Date { get; init; }
        public string StartTime { get; init; } = null!;
    }

    private sealed class RecurringRow
    {
        public Guid DonorUserId { get; init; }
        public string? Language { get; init; }
        public string InstitutionName { get; init; } = null!;
        public string? InstitutionNameSi { get; init; }
        public string? InstitutionNameTa { get; init; }
        public string StartTime { get; init; } = null!;
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

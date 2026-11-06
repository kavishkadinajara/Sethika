using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 08, K.1: the standing sponsors' tables against real SQL Server. Row-Level Security keeps the
/// donor book, the sponsorships and their let-go days to their institution, and a day request to its
/// institution and its asker; the keys and CHECKs make the shapes the later slices rely on the only
/// ones that can be stored.
/// </summary>
/// <remarks>
/// Fixtures are written on a platform connection; every assertion about who sees or writes what runs
/// on a connection stamped as that person. 33504 is SQL Server's error for a block predicate.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class StandingSchemaTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const int BlockPredicate = 33504;

    private readonly List<Guid> _users = [];

    private int _alphaSlot;
    private int _betaSlot;
    private long _alphaSlotInstance;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await using var connection = await PlatformConnectionAsync();
        _alphaSlot = await connection.ExecuteScalarAsync<int>("SELECT Id FROM booking.MealSlots WHERE InstitutionId = @alpha;", new { alpha = database.AlphaInstitutionId });
        _betaSlot = await connection.ExecuteScalarAsync<int>("SELECT Id FROM booking.MealSlots WHERE InstitutionId = @beta;", new { beta = database.BetaInstitutionId });
        _alphaSlotInstance = await connection.ExecuteScalarAsync<long>("SELECT Id FROM booking.SlotInstances WHERE MealSlotId = @slot;", new { slot = _alphaSlot });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync("DELETE FROM auth.Users WHERE Id IN @users;", new { users = _users });
    }

    // ------------------------------------------------------------------ plumbing

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private SqlConnectionFactory ConnectionsFor(ICurrentUserContext user) => new(database.ConnectionString, user);

    private static string RandomPhone() =>
        "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    private async Task<Guid> UserAsync()
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Standing Schema Test', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, phone = RandomPhone() });

        _users.Add(id);
        return id;
    }

    private async Task<int> DonorAsync(int institutionId, Guid? linked = null) =>
        await ScalarAsync<int>(
            """
            INSERT org.InstitutionDonors (InstitutionId, UserId, FullName, Phone, PublicId, CreatedBy)
            VALUES (@institutionId, @linked, N'Regular Donor', @phone, NEWID(), @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { institutionId, linked, phone = RandomPhone(), createdBy = SethikaTestDatabase.AlphaUserId });

    private Task<int> SponsorshipAsync(int institutionId, int donorId, int slotId) =>
        ScalarAsync<int>(
            """
            INSERT booking.StandingSponsorships (PublicId, InstitutionId, InstitutionDonorId, MealSlotId, Pattern, MonthDays, StartDate, Headcount, CreatedBy)
            VALUES (NEWID(), @institutionId, @donorId, @slotId, 'Monthly', 16 | 524288, '2026-12-01', 20, @createdBy);  -- the 5th and the 20th
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { institutionId, donorId, slotId, createdBy = SethikaTestDatabase.AlphaUserId });

    private Task<long> ExceptionAsync(int institutionId, int sponsorshipId, string date) =>
        ScalarAsync<long>(
            """
            INSERT booking.StandingExceptions (InstitutionId, StandingSponsorshipId, ExceptionDate, Kind, Reason, CreatedBy)
            VALUES (@institutionId, @sponsorshipId, @date, 'Skipped', N'Away', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { institutionId, sponsorshipId, date, createdBy = SethikaTestDatabase.AlphaUserId });

    private Task<int> DayRequestAsync(Guid asker, string requestNo, string status = "Open") =>
        ScalarAsync<int>(
            """
            INSERT booking.DayRequests (RequestNo, InstitutionId, MealSlotId, RequestedDate, DonorUserId, Headcount, Status, DecidedBy, DecidedAt)
            VALUES (@requestNo, @alpha, @slot, '2026-12-05', @asker, 10, @status,
                    CASE WHEN @status = 'Open' THEN NULL ELSE @asker END,
                    CASE WHEN @status = 'Open' THEN NULL ELSE SYSUTCDATETIME() END);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { requestNo, alpha = database.AlphaInstitutionId, slot = _alphaSlot, asker, status });

    private async Task<int> SeenAsync(ICurrentUserContext user, string sql, object parameters)
    {
        await using var connection = await ConnectionsFor(user).OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private async Task<SqlException> RefusedAsync(ICurrentUserContext user, string sql, object parameters)
    {
        await using var connection = await ConnectionsFor(user).OpenConnectionAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
    }

    /// <summary>Asserts a platform write is refused by the named constraint or index.</summary>
    private async Task AssertRefusedByAsync(string constraint, string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        var refused = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
        Assert.Contains(constraint, refused.Message, StringComparison.Ordinal);
    }

    private ICurrentUserContext AlphaStaff => TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId);

    private ICurrentUserContext BetaStaff => TestCurrentUserContext.ForInstitution(database.BetaInstitutionId);

    // ------------------------------------------------------------------ Row-Level Security

    [Fact]
    public async Task TheDonorBookAndItsSponsorshipsAreTheirInstitutionsAlone()
    {
        // Linked to an account: the person still reads none of these rows themselves (K1).
        var person = await UserAsync();
        var donor = await DonorAsync(database.AlphaInstitutionId, linked: person);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, donor, _alphaSlot);
        await ExceptionAsync(database.AlphaInstitutionId, sponsorship, "2026-12-05");

        foreach (var table in new[] { "org.InstitutionDonors", "booking.StandingSponsorships", "booking.StandingExceptions" })
        {
            var sql = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(1, await SeenAsync(TestCurrentUserContext.Platform(), sql, new { }));
            Assert.Equal(1, await SeenAsync(AlphaStaff, sql, new { }));
            Assert.Equal(0, await SeenAsync(BetaStaff, sql, new { }));
            Assert.Equal(0, await SeenAsync(TestCurrentUserContext.ForUser(person), sql, new { }));
            Assert.Equal(0, await SeenAsync(TestCurrentUserContext.Anonymous(), sql, new { }));
        }
    }

    [Fact]
    public async Task AnotherInstitutionCannotWriteIntoTheBook()
    {
        var alphaDonor = await DonorAsync(database.AlphaInstitutionId);
        var looseDonor = await DonorAsync(database.AlphaInstitutionId);   // no sponsorship names it
        var betaDonor = await DonorAsync(database.BetaInstitutionId);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, alphaDonor, _alphaSlot);
        var parameters = new
        {
            alpha = database.AlphaInstitutionId,
            beta = database.BetaInstitutionId,
            alphaDonor,
            looseDonor,
            betaDonor,
            alphaSlot = _alphaSlot,
            betaSlot = _betaSlot,
            sponsorship,
            phone = RandomPhone(),
            by = SethikaTestDatabase.AlphaUserId,
        };

        // Into Alpha's book from Beta's session, every key valid: only the block predicate refuses.
        Assert.Equal(BlockPredicate, (await RefusedAsync(BetaStaff,
            "INSERT org.InstitutionDonors (InstitutionId, FullName, Phone, PublicId, CreatedBy) VALUES (@alpha, N'Forged', @phone, NEWID(), @by);",
            parameters)).Number);
        Assert.Equal(BlockPredicate, (await RefusedAsync(BetaStaff,
            """
            INSERT booking.StandingSponsorships (PublicId, InstitutionId, InstitutionDonorId, MealSlotId, Pattern, StartDate, Headcount, CreatedBy)
            VALUES (NEWID(), @alpha, @alphaDonor, @alphaSlot, 'Daily', '2026-12-01', 5, @by);
            """,
            parameters)).Number);
        Assert.Equal(BlockPredicate, (await RefusedAsync(BetaStaff,
            "INSERT booking.StandingExceptions (InstitutionId, StandingSponsorshipId, ExceptionDate, Kind, CreatedBy) VALUES (@alpha, @sponsorship, '2026-12-20', 'Skipped', @by);",
            parameters)).Number);

        // Nor can Alpha hand its rows to Beta. A donor no sponsorship names, so no key is in the way.
        Assert.Equal(BlockPredicate, (await RefusedAsync(AlphaStaff,
            "UPDATE org.InstitutionDonors SET InstitutionId = @beta WHERE Id = @looseDonor;",
            parameters)).Number);
        // Every key moved with it, so again only the block predicate refuses.
        Assert.Equal(BlockPredicate, (await RefusedAsync(AlphaStaff,
            "UPDATE booking.StandingSponsorships SET InstitutionId = @beta, InstitutionDonorId = @betaDonor, MealSlotId = @betaSlot WHERE Id = @sponsorship;",
            parameters)).Number);

        var betaSponsorship = await SponsorshipAsync(database.BetaInstitutionId, betaDonor, _betaSlot);
        var exception = await ExceptionAsync(database.AlphaInstitutionId, sponsorship, "2026-12-05");
        Assert.Equal(BlockPredicate, (await RefusedAsync(AlphaStaff,
            "UPDATE booking.StandingExceptions SET InstitutionId = @beta, StandingSponsorshipId = @betaSponsorship WHERE Id = @exception;",
            new { parameters.beta, betaSponsorship, exception })).Number);

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM org.InstitutionDonors WHERE InstitutionId = @alpha;", parameters));
    }

    [Fact]
    public async Task ADayRequestIsItsInstitutionsAndItsAskersAlone()
    {
        var asker = await UserAsync();
        var stranger = await UserAsync();
        await DayRequestAsync(asker, "DR-TEST-1");

        const string Count = "SELECT COUNT(*) FROM booking.DayRequests;";
        Assert.Equal(1, await SeenAsync(TestCurrentUserContext.Platform(), Count, new { }));
        Assert.Equal(1, await SeenAsync(AlphaStaff, Count, new { }));
        Assert.Equal(1, await SeenAsync(TestCurrentUserContext.ForUser(asker), Count, new { }));
        Assert.Equal(0, await SeenAsync(BetaStaff, Count, new { }));
        Assert.Equal(0, await SeenAsync(TestCurrentUserContext.ForUser(stranger), Count, new { }));
        Assert.Equal(0, await SeenAsync(TestCurrentUserContext.Anonymous(), Count, new { }));

        var parameters = new { alpha = database.AlphaInstitutionId, slot = _alphaSlot, asker, stranger };

        // A person asks in their own name...
        await using (var connection = await ConnectionsFor(TestCurrentUserContext.ForUser(asker)).OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT booking.DayRequests (RequestNo, InstitutionId, MealSlotId, RequestedDate, DonorUserId, Headcount) VALUES ('DR-TEST-2', @alpha, @slot, '2026-12-20', @asker, 5);",
                parameters);
        }

        // ...never in someone else's, and cannot hand theirs to someone else.
        Assert.Equal(BlockPredicate, (await RefusedAsync(TestCurrentUserContext.ForUser(stranger),
            "INSERT booking.DayRequests (RequestNo, InstitutionId, MealSlotId, RequestedDate, DonorUserId, Headcount) VALUES ('DR-TEST-3', @alpha, @slot, '2026-12-21', @asker, 5);",
            parameters)).Number);
        Assert.Equal(BlockPredicate, (await RefusedAsync(TestCurrentUserContext.ForUser(asker),
            "UPDATE booking.DayRequests SET DonorUserId = @stranger WHERE RequestNo = 'DR-TEST-2';",
            parameters)).Number);

        // Another institution cannot write one into Alpha's tray.
        Assert.Equal(BlockPredicate, (await RefusedAsync(BetaStaff,
            "INSERT booking.DayRequests (RequestNo, InstitutionId, MealSlotId, RequestedDate, DonorUserId, Headcount) VALUES ('DR-TEST-4', @alpha, @slot, '2026-12-22', @asker, 5);",
            parameters)).Number);

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.DayRequests WHERE DonorUserId = @asker;", parameters));
    }

    // ------------------------------------------------------------------ shapes

    [Fact]
    public async Task EachPatternHasItsOwnFieldsAndNoOthers()
    {
        var donor = await DonorAsync(database.AlphaInstitutionId);
        const string Insert =
            """
            INSERT booking.StandingSponsorships
                (PublicId, InstitutionId, InstitutionDonorId, MealSlotId, Pattern, MonthInterval, WeekDays, MonthDays, YearMonth, YearDay, StartDate, EndDate, Headcount, CreatedBy)
            VALUES (NEWID(), @alpha, @donor, @slot, @pattern, @interval, @weekDays, @monthDays, @yearMonth, @yearDay, '2026-12-01', @end, 10, @by);
            """;

        object Row(string pattern, byte interval = 1, byte? weekDays = null, int? monthDays = null, byte? yearMonth = null, byte? yearDay = null, string? end = null) =>
            new { alpha = database.AlphaInstitutionId, donor, slot = _alphaSlot, pattern, interval, weekDays, monthDays, yearMonth, yearDay, end, by = SethikaTestDatabase.AlphaUserId };

        // Every pattern, well formed.
        await ExecuteAsync(Insert, Row("Once", end: "2026-12-01"));
        await ExecuteAsync(Insert, Row("Daily"));
        await ExecuteAsync(Insert, Row("Weekly", weekDays: 1 | 64));                 // Sunday and Saturday
        await ExecuteAsync(Insert, Row("Monthly", interval: 3, monthDays: 1 << 30)); // the 31st, quarterly
        await ExecuteAsync(Insert, Row("Yearly", yearMonth: 2, yearDay: 29));        // skipped in common years (Q3)
        await ExecuteAsync(Insert, Row("Yearly", yearMonth: 4, yearDay: 30));

        // Each missing its own field, or carrying another's.
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Once"));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Once", end: "2026-12-02"));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Daily", interval: 2));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Daily", weekDays: 1));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Weekly"));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Weekly", weekDays: 2, monthDays: 1));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Monthly"));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Monthly", monthDays: 1, yearDay: 1));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Yearly", yearMonth: 5));
        await AssertRefusedByAsync("CK_Standing_Shape", Insert, Row("Yearly", yearMonth: 5, yearDay: 5, interval: 2));

        // A day no year has; masks and intervals out of range.
        await AssertRefusedByAsync("CK_Standing_YearDate", Insert, Row("Yearly", yearMonth: 4, yearDay: 31));
        await AssertRefusedByAsync("CK_Standing_YearDate", Insert, Row("Yearly", yearMonth: 2, yearDay: 30));
        await AssertRefusedByAsync("CK_Standing_WeekDays", Insert, Row("Weekly", weekDays: 128));
        await AssertRefusedByAsync("CK_Standing_WeekDays", Insert, Row("Weekly", weekDays: 0));
        await AssertRefusedByAsync("CK_Standing_MonthDays", Insert, Row("Monthly", monthDays: 0));
        await AssertRefusedByAsync("CK_Standing_MonthDays", Insert, Row("Monthly", monthDays: int.MinValue));
        await AssertRefusedByAsync("CK_Standing_MonthInterval", Insert, Row("Monthly", interval: 13, monthDays: 1));
        await AssertRefusedByAsync("CK_Standing_Dates", Insert, Row("Daily", end: "2026-11-30"));

        Assert.Equal(6, await ScalarAsync<int>("SELECT COUNT(*) FROM booking.StandingSponsorships;", new { }));
    }

    [Fact]
    public async Task AnEndedSponsorshipSaysWhoEndedItAndWhen()
    {
        var donor = await DonorAsync(database.AlphaInstitutionId);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, donor, _alphaSlot);
        var parameters = new { sponsorship, by = SethikaTestDatabase.AlphaUserId };

        await AssertRefusedByAsync("CK_Standing_Ended", "UPDATE booking.StandingSponsorships SET Status = 'Ended' WHERE Id = @sponsorship;", parameters);
        await AssertRefusedByAsync("CK_Standing_Ended", "UPDATE booking.StandingSponsorships SET EndedAt = SYSUTCDATETIME(), EndedBy = @by WHERE Id = @sponsorship;", parameters);
        await AssertRefusedByAsync("CK_Standing_Ended", "UPDATE booking.StandingSponsorships SET Status = 'Ended', EndedAt = SYSUTCDATETIME() WHERE Id = @sponsorship;", parameters);
        await AssertRefusedByAsync("CK_Standing_Ended", "UPDATE booking.StandingSponsorships SET Status = 'Ended', EndedBy = @by WHERE Id = @sponsorship;", parameters);
        await ExecuteAsync("UPDATE booking.StandingSponsorships SET Status = 'Ended', EndedAt = SYSUTCDATETIME(), EndedBy = @by WHERE Id = @sponsorship;", parameters);
    }

    [Fact]
    public async Task TheTextConsentHasItsWhoAndWhen()
    {
        var donor = await DonorAsync(database.AlphaInstitutionId);
        var parameters = new { donor, by = SethikaTestDatabase.AlphaUserId };

        await AssertRefusedByAsync("CK_InstDonor_TextConsent", "UPDATE org.InstitutionDonors SET TextConsentAt = SYSUTCDATETIME() WHERE Id = @donor;", parameters);
        await AssertRefusedByAsync("CK_InstDonor_TextConsent", "UPDATE org.InstitutionDonors SET TextConsentBy = @by WHERE Id = @donor;", parameters);
        await AssertRefusedByAsync("CK_InstDonor_Language", "UPDATE org.InstitutionDonors SET Language = 'fr' WHERE Id = @donor;", parameters);
        await ExecuteAsync("UPDATE org.InstitutionDonors SET TextConsentAt = SYSUTCDATETIME(), TextConsentBy = @by WHERE Id = @donor;", parameters);
    }

    [Fact]
    public async Task AStandingBookingIsTheSponsorshipsOwnDonors()
    {
        var donor = await DonorAsync(database.AlphaInstitutionId);
        var other = await DonorAsync(database.AlphaInstitutionId);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, donor, _alphaSlot);
        const string Insert =
            """
            INSERT booking.Bookings (BookingNo, InstitutionId, SlotInstanceId, InstitutionDonorId, DonorName, DonorPhone, Headcount, Source, StandingSponsorshipId, CreatedBy)
            VALUES (@no, @alpha, @instance, @donorId, N'Regular Donor', @phone, 10, @source, @sponsorshipId, @by);
            """;

        object Row(string no, string source, int? donorId, int? sponsorshipId) =>
            new { no, alpha = database.AlphaInstitutionId, instance = _alphaSlotInstance, donorId, phone = RandomPhone(), source, sponsorshipId, by = SethikaTestDatabase.AlphaUserId };

        await AssertRefusedByAsync("CK_Bookings_Standing", Insert, Row("BK-ST-1", "Standing", donor, null));
        await AssertRefusedByAsync("CK_Bookings_Standing", Insert, Row("BK-ST-2", "Standing", null, sponsorship));
        await AssertRefusedByAsync("CK_Bookings_Standing", Insert, Row("BK-ST-3", "Staff", donor, sponsorship));
        await AssertRefusedByAsync("FK_Bookings_StandingTenant", Insert, Row("BK-ST-4", "Standing", other, sponsorship));

        await ExecuteAsync(Insert, Row("BK-ST-5", "Standing", donor, sponsorship));
        await ExecuteAsync(Insert, Row("BK-ST-6", "Staff", other, null));
    }

    [Fact]
    public async Task ADayIsLetGoOnceAndEachKindNamesWhatItNeeds()
    {
        var donor = await DonorAsync(database.AlphaInstitutionId);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, donor, _alphaSlot);
        var asker = await UserAsync();
        var request = await DayRequestAsync(asker, "DR-TEST-5", status: "Withdrawn");
        const string Insert =
            """
            INSERT booking.StandingExceptions (InstitutionId, StandingSponsorshipId, ExceptionDate, Kind, OtherDate, DayRequestId, CreatedBy)
            VALUES (@alpha, @sponsorship, @date, @kind, @other, @request, @by);
            """;

        object Row(string date, string kind, string? other = null, int? requestId = null) =>
            new { alpha = database.AlphaInstitutionId, sponsorship, date, kind, other, request = requestId, by = SethikaTestDatabase.AlphaUserId };

        await ExecuteAsync(Insert, Row("2026-12-05", "Skipped"));
        await AssertRefusedByAsync("UQ_StandingExc_Day", Insert, Row("2026-12-05", "Blocked"));

        await AssertRefusedByAsync("CK_StandingExc_Swap", Insert, Row("2027-01-05", "MovedTo"));
        await AssertRefusedByAsync("CK_StandingExc_Swap", Insert, Row("2027-01-05", "MovedFrom", other: "2027-01-05"));
        await AssertRefusedByAsync("CK_StandingExc_Swap", Insert, Row("2027-01-05", "Blocked", other: "2027-01-12"));
        await AssertRefusedByAsync("CK_StandingExc_Request", Insert, Row("2027-01-05", "Released"));
        await AssertRefusedByAsync("CK_StandingExc_Request", Insert, Row("2027-01-05", "Skipped", requestId: request));

        await ExecuteAsync(Insert, Row("2027-01-05", "MovedTo", other: "2027-01-12"));
        await ExecuteAsync(Insert, Row("2027-01-20", "Released", requestId: request));
    }

    [Fact]
    public async Task ADonorHasOneOpenRequestPerSlotDayAndEachAnswerItsWhoAndWhen()
    {
        var asker = await UserAsync();
        var request = await DayRequestAsync(asker, "DR-TEST-6");

        await AssertRefusedByAsync(
            "UX_DayRequests_OpenPerDonor",
            "INSERT booking.DayRequests (RequestNo, InstitutionId, MealSlotId, RequestedDate, DonorUserId, Headcount) VALUES ('DR-TEST-7', @alpha, @slot, '2026-12-05', @asker, 5);",
            new { alpha = database.AlphaInstitutionId, slot = _alphaSlot, asker });

        // Given names both the day let go and the booking made; each alone is refused.
        var donor = await DonorAsync(database.AlphaInstitutionId);
        var sponsorship = await SponsorshipAsync(database.AlphaInstitutionId, donor, _alphaSlot);
        var booking = await ScalarAsync<long>("SELECT Id FROM booking.Bookings WHERE BookingNo = 'BK-ALPHA';", new { });
        var parameters = new { request, asker, sponsorship, booking };
        const string Given = "UPDATE booking.DayRequests SET Status = 'Given', DecidedBy = @asker, DecidedAt = SYSUTCDATETIME()";
        await AssertRefusedByAsync("CK_DayRequests_Decision", "UPDATE booking.DayRequests SET Status = 'Declined' WHERE Id = @request;", parameters);
        await AssertRefusedByAsync("CK_DayRequests_Decision", Given + ", StandingSponsorshipId = @sponsorship WHERE Id = @request;", parameters);
        await AssertRefusedByAsync("CK_DayRequests_Decision", Given + ", BookingId = @booking WHERE Id = @request;", parameters);
        await AssertRefusedByAsync("CK_DayRequests_Decision", "UPDATE booking.DayRequests SET StandingSponsorshipId = @sponsorship WHERE Id = @request;", parameters);
        await AssertRefusedByAsync("CK_DayRequests_DeclineReason", "UPDATE booking.DayRequests SET DeclineReason = N'No' WHERE Id = @request;", parameters);
        await ExecuteAsync("UPDATE booking.DayRequests SET Status = 'Declined', DecidedBy = @asker, DecidedAt = SYSUTCDATETIME(), DeclineReason = N'Held' WHERE Id = @request;", parameters);

        // Answered, it no longer stands in the way of asking again.
        await DayRequestAsync(asker, "DR-TEST-8");
    }
}

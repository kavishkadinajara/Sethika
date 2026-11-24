using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Messaging;

/// <summary>
/// The service texts (Phase E, 8.8, V14, V15) against real SQL Server: what the outbox texts the donor
/// and the leaders, in whose language, at which number, and the evening reminder - each once.
/// </summary>
/// <remarks>
/// Appointments are made and changed through the real handlers, so the outbox rows are the ones they
/// write; the processor here holds only the appointment handlers. Texts go through the console
/// sender, which records each in <c>notify.Notifications</c> under its dedup key.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class AppointmentSmsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string NameSi = "ඇල්ෆා විහාරය";
    private const string NameTa = "அல்பா விகாரை";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];
    private readonly List<SethikaDbContext> _contexts = [];

    private static readonly string[] AssignedOnly = [AppointmentTexts.LeaderAssigned];
    private static readonly string[] AssignedThenRemoved = [AppointmentTexts.LeaderAssigned, AppointmentTexts.LeaderRemoved];

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;
            UPDATE org.Institutions SET NameSi = @NameSi, NameTa = @NameTa WHERE Id = @alpha;
            INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
            SELECT @alpha, m.Id, 1, 'Manual', @changedBy FROM auth.Modules m WHERE m.Code = 'SERVICES';
            """,
            new { NameSi, NameTa, alpha = database.AlphaInstitutionId, changedBy = SethikaTestDatabase.AlphaUserId });
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM core.OutboxMessages WHERE AggregateType = 'ServiceAppointment';
            DELETE FROM notify.Notifications WHERE Recipient IN @phones;
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { phones = _phones, users = _users });
    }

    // ------------------------------------------------------------------ plumbing

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private SqlConnectionFactory PlatformConnections() => new(database.ConnectionString, TestCurrentUserContext.Platform());

    private SethikaDbContext ContextFor(ICurrentUserContext user)
    {
        var context = new SethikaDbContext(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);
        _contexts.Add(context);
        return context;
    }

    private AppointmentTexts Texts()
    {
        var connections = PlatformConnections();
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        return new AppointmentTexts(new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance));
    }

    private OutboxProcessor Processor()
    {
        var queries = new AppointmentNotificationQueries(PlatformConnections());
        return new(
            PlatformConnections(),
            [
                new AppointmentSmsHandler(queries, Texts()),
                new AppointmentCancelledHandler(ContextFor(TestCurrentUserContext.Platform()), queries, Texts()),
            ],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
    }

    private AppointmentReminders Reminders() => new(new AppointmentNotificationQueries(PlatformConnections()), Texts(), TimeProvider.System);

    private string NewPhone(string prefix = "+9476")
    {
        var phone = prefix + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        return phone;
    }

    private async Task<(Guid Id, string Phone)> AccountAsync(string language = "en", bool confirmed = true)
    {
        var id = Guid.CreateVersion7();
        var phone = NewPhone("+9475");
        _users.Add(id);
        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Text Donor', @phone, @confirmed, @language, 'Active', 'stamp');
            """,
            new { id, phone, language, confirmed });
        return (id, phone);
    }

    private async Task<Guid> TypeAsync()
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.ServiceTypes (PublicId, InstitutionId, Name, DurationMinutes, BufferMinutes, LeadersRequired, LocationMode, MinAdvanceDays, CreatedBy)
            VALUES (@publicId, @alpha, @name, 120, 0, 3, 'AtInstitution', 0, @createdBy);
            """,
            new { publicId, alpha = database.AlphaInstitutionId, name = "Bana " + Guid.NewGuid().ToString("N")[..8], createdBy = SethikaTestDatabase.AlphaUserId });
        return publicId;
    }

    /// <summary>A leader on Alpha's roster with a phone (or none) and a language, perhaps linked to an account.</summary>
    private async Task<(Guid PublicId, int Id)> LeaderAsync(string? phone, string language = "si", Guid? userId = null)
    {
        var publicId = Guid.CreateVersion7();
        var id = await ScalarAsync<int>(
            """
            INSERT org.ReligiousLeaders (PublicId, InstitutionId, UserId, DisplayName, Phone, Language, IsActive, CreatedBy)
            VALUES (@publicId, @alpha, @userId, N'Ven. Text Thero', @phone, @language, 1, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { publicId, alpha = database.AlphaInstitutionId, userId, phone, language, createdBy = SethikaTestDatabase.AlphaUserId });
        return (publicId, id);
    }

    /// <summary>A request through the real handler, at 10:00 Colombo on <paramref name="day"/>; its number.</summary>
    private async Task<string> RequestAsync(Guid donor, Guid type, DateOnly day)
    {
        var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var result = await new RequestServiceHandler(context, new SqlInstitutionModules(context), new SqlNumberSeries(context), TimeProvider.System)
            .RequestAsync(donor, new RequestServiceCommand(type, day, new TimeOnly(10, 0), ServiceLocation.AtInstitution, null, null, null));

        Assert.True(result.Succeeded, result.Error);
        return result.AppointmentNo!;
    }

    private async Task AssignAsync(string appointmentNo, params Guid[] leaders)
    {
        var result = await new AssignLeadersHandler(ContextFor(TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId)), new NoLocks(), TimeProvider.System)
            .AssignAsync(database.AlphaInstitutionId, appointmentNo, leaders, SethikaTestDatabase.AlphaUserId);
        Assert.True(result.Succeeded, result.Error);
    }

    private AppointmentDecisionHandler Decisions(ICurrentUserContext user) => new(ContextFor(user), TimeProvider.System);

    private AppointmentDecisionHandler Institution() => Decisions(TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId));

    private static void Succeeded(AppointmentResult result) => Assert.True(result.Succeeded, result.Error);

    private sealed class NoLocks : ILeaderLocks
    {
        public Task<ILockLease> AcquireAsync(IEnumerable<int> leaderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<ILockLease>(new Unlocked());

        private sealed class Unlocked : ILockLease
        {
            public LockOutcome Outcome => LockOutcome.Unavailable;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed record Sent(string Body, string DedupKey);

    private async Task<List<Sent>> SentToAsync(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return [.. await connection.QueryAsync<Sent>(
            "SELECT Body AS Body, DedupKey AS DedupKey FROM notify.Notifications WHERE Recipient = @phone ORDER BY Id;",
            new { phone })];
    }

    /// <summary>The template's text as a reader in <paramref name="language"/> should read it, for a service at 10:00 Colombo.</summary>
    private async Task<string> ExpectedAsync(string template, string language, string appointmentNo, DateOnly day, string time = "10:00")
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var body = await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @template AND Channel = 'SMS' AND Language = @language;",
            new { template, language }) ?? throw new InvalidOperationException($"{template} {language} is not seeded.");

        var institution = language switch { "si" => NameSi, "ta" => NameTa, _ => "Alpha Temple" };
        return body
            .Replace("{appointmentNo}", appointmentNo, StringComparison.Ordinal)
            .Replace("{institution}", institution, StringComparison.Ordinal)
            .Replace("{date}", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", time, StringComparison.Ordinal);
    }

    private Task<long> MessageIdAsync(string appointmentNo, string eventType, int leaderId) =>
        ScalarAsync<long>(
            "SELECT MAX(Id) FROM core.OutboxMessages WHERE AggregateId = @appointmentNo AND EventType = @eventType AND JSON_VALUE(Payload, '$.leaderId') = @leaderId;",
            new { appointmentNo, eventType, leaderId = leaderId.ToString(CultureInfo.InvariantCulture) });

    private Task<int> UnprocessedAsync() =>
        ScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE ProcessedAt IS NULL;", new { });

    // ------------------------------------------------------------------ the outbox texts

    [Theory]
    [InlineData("en")]
    [InlineData("si")]
    [InlineData("ta")]
    public async Task TheDonorHearsOfTheRequestAndTheApproval_InTheirLanguage_AndTheLeaderInTheirs(string language)
    {
        var (donor, donorPhone) = await AccountAsync(language);
        var leaderPhone = NewPhone();
        var leader = await LeaderAsync(leaderPhone, language: "ta");
        var day = Today.AddDays(5);
        var no = await RequestAsync(donor, await TypeAsync(), day);
        await AssignAsync(no, leader.PublicId);

        await Processor().RunAsync();

        Assert.Equal(
            new[]
            {
                new Sent(await ExpectedAsync(AppointmentTexts.Requested, language, no, day), $"APPOINTMENT_REQUESTED:{no}"),
                new Sent(await ExpectedAsync(AppointmentTexts.Approved, language, no, day), $"APPOINTMENT_APPROVED:{no}"),
            },
            await SentToAsync(donorPhone));

        var assigned = await MessageIdAsync(no, AssignLeadersHandler.AssignedEvent, leader.Id);
        Assert.Equal(
            new[] { new Sent(await ExpectedAsync(AppointmentTexts.LeaderAssigned, "ta", no, day), $"LEADER_ASSIGNED:{no}:{leader.Id}:{assigned}") },
            await SentToAsync(leaderPhone));
        Assert.Equal(0, await UnprocessedAsync());
    }

    [Fact]
    public async Task ARejectionIsTextedOnce_HoweverOftenItsMessageIsHandedOver()
    {
        var (donor, phone) = await AccountAsync();
        var day = Today.AddDays(4);
        var no = await RequestAsync(donor, await TypeAsync(), day);
        Succeeded(await Institution().DecideAsync(database.AlphaInstitutionId, no, AppointmentDecision.Reject, "Away", SethikaTestDatabase.AlphaUserId));

        await Processor().RunAsync();
        await ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = NULL WHERE AggregateId = @no;", new { no });
        await Processor().RunAsync();

        Assert.Equal(
            new[] { $"APPOINTMENT_REQUESTED:{no}", $"APPOINTMENT_REJECTED:{no}" },
            (await SentToAsync(phone)).Select(s => s.DedupKey).ToArray());
        Assert.Equal(await ExpectedAsync(AppointmentTexts.Rejected, "en", no, day), (await SentToAsync(phone))[1].Body);
    }

    [Fact]
    public async Task AnInstitutionsCancellationIsTextedToTheDonorAndToEveryLeaderStillExpected()
    {
        var (donor, donorPhone) = await AccountAsync("si");
        var (stays, declines, removed) = (NewPhone(), NewPhone(), NewPhone());
        var a = await LeaderAsync(stays, "en");
        var b = await LeaderAsync(declines, "en");
        var c = await LeaderAsync(removed, "en");
        var day = Today.AddDays(6);
        var no = await RequestAsync(donor, await TypeAsync(), day);
        await AssignAsync(no, a.PublicId, b.PublicId, c.PublicId);
        Succeeded(await Institution().RespondOnBehalfAsync(database.AlphaInstitutionId, no, b.PublicId, accept: false, null, SethikaTestDatabase.AlphaUserId));
        Succeeded(await Institution().RemoveLeaderAsync(database.AlphaInstitutionId, no, c.PublicId, SethikaTestDatabase.AlphaUserId));
        Succeeded(await Institution().DecideAsync(database.AlphaInstitutionId, no, AppointmentDecision.Cancel, "Flooded", SethikaTestDatabase.AlphaUserId));

        await Processor().RunAsync();

        Assert.Equal(
            new Sent(await ExpectedAsync(AppointmentTexts.Cancelled, "si", no, day), $"APPOINTMENT_CANCELLED:{no}"),
            (await SentToAsync(donorPhone)).Last());
        Assert.Equal(
            new Sent(await ExpectedAsync(AppointmentTexts.LeaderCancelled, "en", no, day), $"LEADER_CANCELLED:{no}:{a.Id}"),
            (await SentToAsync(stays)).Last());

        // The one who declined hears only of the assignment; the one removed, of that and the removal.
        Assert.Equal(AssignedOnly, (await SentToAsync(declines)).Select(s => s.DedupKey.Split(':')[0]).ToArray());
        Assert.Equal(AssignedThenRemoved, (await SentToAsync(removed)).Select(s => s.DedupKey.Split(':')[0]).ToArray());
        Assert.Equal(0, await UnprocessedAsync());
    }

    [Fact]
    public async Task ADonorsOwnCancellationIsNotTextedBackButTheLeadersHearOfIt()
    {
        var (donor, donorPhone) = await AccountAsync();
        var leaderPhone = NewPhone();
        var leader = await LeaderAsync(leaderPhone, "en");
        var no = await RequestAsync(donor, await TypeAsync(), Today.AddDays(7));
        await AssignAsync(no, leader.PublicId);
        Succeeded(await Decisions(TestCurrentUserContext.ForUser(donor)).CancelAsDonorAsync(no, null, donor));

        await Processor().RunAsync();

        Assert.DoesNotContain(await SentToAsync(donorPhone), s => s.DedupKey.StartsWith("APPOINTMENT_CANCELLED", StringComparison.Ordinal));
        Assert.Contains(await SentToAsync(leaderPhone), s => s.DedupKey == $"LEADER_CANCELLED:{no}:{leader.Id}");
    }

    [Fact]
    public async Task ALeaderReassignedHearsEachTime_AndOneWithoutAPhoneIsSkipped()
    {
        var (donor, _) = await AccountAsync();
        var phone = NewPhone();
        var leader = await LeaderAsync(phone, "en");
        var silent = await LeaderAsync(phone: null);
        var no = await RequestAsync(donor, await TypeAsync(), Today.AddDays(8));
        await AssignAsync(no, leader.PublicId, silent.PublicId);
        Succeeded(await Institution().RespondOnBehalfAsync(database.AlphaInstitutionId, no, leader.PublicId, accept: false, null, SethikaTestDatabase.AlphaUserId));
        await AssignAsync(no, leader.PublicId);

        var run = await Processor().RunAsync();

        Assert.Equal(0, run.Failed);
        Assert.Equal(2, (await SentToAsync(phone)).Count(s => s.DedupKey.StartsWith("LEADER_ASSIGNED:", StringComparison.Ordinal)));
        Assert.Equal(0, await UnprocessedAsync());
    }

    [Fact]
    public async Task ALinkedLeadersAccountGivesTheLanguage_AndItsConfirmedPhoneWhenTheRosterHasNone()
    {
        var (donor, _) = await AccountAsync();
        var (account, accountPhone) = await AccountAsync("ta");
        var linked = await LeaderAsync(phone: null, language: "si", userId: account);
        var day = Today.AddDays(9);
        var no = await RequestAsync(donor, await TypeAsync(), day);
        await AssignAsync(no, linked.PublicId);

        await Processor().RunAsync();

        Assert.Equal(await ExpectedAsync(AppointmentTexts.LeaderAssigned, "ta", no, day), Assert.Single(await SentToAsync(accountPhone)).Body);

        // An unconfirmed account number is never used: it may be someone else's.
        var (unconfirmed, unconfirmedPhone) = await AccountAsync("ta", confirmed: false);
        var other = await LeaderAsync(phone: null, userId: unconfirmed);
        var second = await RequestAsync(donor, await TypeAsync(), day);
        await AssignAsync(second, other.PublicId);

        await Processor().RunAsync();

        Assert.Empty(await SentToAsync(unconfirmedPhone));
    }

    // ------------------------------------------------------------------ the evening reminder

    /// <summary>An approved appointment at a Colombo time, written directly, with leaders and their answers.</summary>
    private async Task<string> ApprovedAsync(Guid donor, Guid type, DateOnly day, int hour, int minute, string status = AppointmentStatus.Approved, params (int LeaderId, string Response)[] leaders)
    {
        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        var startsAt = SriLankaTime.ToUtc(day, new TimeOnly(hour, minute));
        var id = await ScalarAsync<long>(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, Status, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Text Donor', '+94770000000', @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt),
                   'AtInstitution', @status, @donor
            FROM booking.ServiceTypes WHERE PublicId = @type;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { no, donor, type, startsAt, status });

        foreach (var (leaderId, response) in leaders)
        {
            await ExecuteAsync(
                """
                INSERT booking.AppointmentLeaders (AppointmentId, LeaderId, InstitutionId, Response, AssignedBy)
                VALUES (@id, @leaderId, @alpha, @response, @by);
                """,
                new { id, leaderId, alpha = database.AlphaInstitutionId, response, by = SethikaTestDatabase.AlphaUserId });
        }

        return no;
    }

    [Fact]
    public async Task TheEveningReminderGoesToTomorrowsDonorsAndLeadersOnce()
    {
        var (donor, donorPhone) = await AccountAsync("si");
        var type = await TypeAsync();
        var (acceptedPhone, declinedPhone) = (NewPhone(), NewPhone());
        var accepted = await LeaderAsync(acceptedPhone, "en");
        var declined = await LeaderAsync(declinedPhone, "en");
        var tomorrow = Today.AddDays(1);

        // 05:00 tomorrow in Colombo is still today in UTC; 23:45 is tomorrow's last quarter.
        var early = await ApprovedAsync(donor, type, tomorrow, 5, 0, leaders: [(accepted.Id, AssignmentResponse.Accepted), (declined.Id, AssignmentResponse.Declined)]);
        var late = await ApprovedAsync(donor, type, tomorrow, 23, 45);
        var dayAfter = await ApprovedAsync(donor, type, tomorrow.AddDays(1), 0, 0);
        var waiting = await ApprovedAsync(donor, type, tomorrow, 9, 0, AppointmentStatus.Requested);
        var today = await ApprovedAsync(donor, type, Today, 23, 45);

        var run = await Reminders().SendForTomorrowAsync();

        Assert.Equal(new AppointmentReminderRun(tomorrow, 2, 3, 0), run);
        Assert.Equal(
            new[]
            {
                new Sent(await ExpectedAsync(AppointmentTexts.Reminder, "si", early, tomorrow, "05:00"), $"appt-reminder:{early}:donor"),
                new Sent(await ExpectedAsync(AppointmentTexts.Reminder, "si", late, tomorrow, "23:45"), $"appt-reminder:{late}:donor"),
            },
            await SentToAsync(donorPhone));
        Assert.Equal(
            new[] { new Sent(await ExpectedAsync(AppointmentTexts.LeaderReminder, "en", early, tomorrow, "05:00"), $"appt-reminder:{early}:leader-{accepted.Id}") },
            await SentToAsync(acceptedPhone));
        Assert.Empty(await SentToAsync(declinedPhone));
        Assert.DoesNotContain(await SentToAsync(donorPhone), s => s.DedupKey.Contains(dayAfter, StringComparison.Ordinal) || s.DedupKey.Contains(waiting, StringComparison.Ordinal) || s.DedupKey.Contains(today, StringComparison.Ordinal));

        // A second run, a retry or another instance: nobody is texted twice.
        await Reminders().SendForTomorrowAsync();
        Assert.Equal(2, (await SentToAsync(donorPhone)).Count);
        Assert.Single(await SentToAsync(acceptedPhone));
    }
}

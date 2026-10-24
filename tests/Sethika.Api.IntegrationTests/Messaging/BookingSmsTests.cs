using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Notifications;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Messaging;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Messaging;

/// <summary>
/// Plan 03, 5.6: the booking texts end to end below HTTP - a booking or decision through the real
/// handlers, its outbox row, the processor, <see cref="BookingSmsHandler"/>, the seeded templates and
/// the console sender writing <c>notify.Notifications</c> - and the evening reminder.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class BookingSmsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string NameSi = "ඇල්ෆා විහාරය";
    private const string NameTa = "அல்பா விகாரை";

    private readonly List<Guid> _users = [];
    private readonly List<string> _phones = [];

    private static DateOnly Today => SriLankaTime.Today(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;
            UPDATE org.Institutions SET NameSi = @NameSi, NameTa = @NameTa WHERE Id = @alpha;
            """,
            new { NameSi, NameTa, alpha = database.AlphaInstitutionId });
    }

    public async Task DisposeAsync()
    {
        await database.SeedAsync();
        await ExecuteAsync(
            """
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

    private SqlConnectionFactory PlatformConnections() => new(database.ConnectionString, TestCurrentUserContext.Platform());

    private UserTextMessages Texts()
    {
        var connections = PlatformConnections();
        var sender = new ConsoleSmsSender(connections, Options.Create(new SmsOptions()), NullLogger<ConsoleSmsSender>.Instance);
        return new UserTextMessages(connections, sender, NullLogger<UserTextMessages>.Instance);
    }

    private OutboxProcessor Processor() =>
        new(
            PlatformConnections(),
            [new BookingSmsHandler(new BookingNotificationQueries(PlatformConnections()), Texts())],
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

    private BookingReminders Reminders() => new(new BookingNotificationQueries(PlatformConnections()), Texts(), TimeProvider.System);

    private async Task<(Guid Id, string Phone)> DonorAsync(string language = "en")
    {
        var id = Guid.CreateVersion7();
        var phone = "+9475" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);
        _phones.Add(phone);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Text Donor', @phone, 1, @language, 'Active', 'stamp');
            """,
            new { id, phone, language });

        return (id, phone);
    }

    /// <summary>
    /// After booking, which needs a confirmed phone: a text goes to the account's confirmed number,
    /// never to the booking's snapshot of it.
    /// </summary>
    private Task UnconfirmPhoneAsync(Guid user) =>
        ExecuteAsync("UPDATE auth.Users SET PhoneConfirmed = 0 WHERE Id = @user;", new { user });

    private async Task<Guid> SlotAsync(bool requiresApproval = true, string mode = "Shared", byte maxSponsors = 10)
    {
        var publicId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT booking.MealSlots
                (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors, MinHeadcount, MaxHeadcount,
                 MinAdvanceDays, MaxAdvanceDays, RequiresApproval, CreatedBy)
            VALUES (@publicId, @alpha, @name, '11:30', '13:00', @mode, @maxSponsors, 1, 200, 1, 90, @requiresApproval, @createdBy);
            """,
            new
            {
                publicId,
                alpha = database.AlphaInstitutionId,
                name = "Dana " + Guid.NewGuid().ToString("N")[..8],
                mode,
                maxSponsors,
                requiresApproval,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return publicId;
    }

    private SethikaDbContext ContextFor(ICurrentUserContext user) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);

    /// <summary>A booking by <paramref name="donor"/> through the engine, which writes its outbox row.</summary>
    private async Task<string> BookAsync(Guid donor, Guid slot, DateOnly date)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var handler = new BookMealHandler(context, new NoLocks(), new SqlSlotInventory(context), new SqlNumberSeries(context), TimeProvider.System);
        var result = await handler.BookAsync(donor, new BookMealCommand(slot, date, 10, FulfilmentMode.DonorBrings, null, null));

        Assert.True(result.Succeeded, result.Error);
        return result.BookingNo!;
    }

    private async Task DecideAsync(string bookingNo, InstitutionDecision decision, string? reason = null)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId));
        var result = await new BookingDecisionHandler(context, new SqlSlotInventory(context), TimeProvider.System)
            .DecideAsync(database.AlphaInstitutionId, bookingNo, decision, reason, SethikaTestDatabase.AlphaUserId);

        Assert.True(result.Succeeded, result.Error);
    }

    private async Task CancelAsDonorAsync(Guid donor, string bookingNo)
    {
        await using var context = ContextFor(TestCurrentUserContext.ForUser(donor));
        var result = await new BookingDecisionHandler(context, new SqlSlotInventory(context), TimeProvider.System)
            .CancelAsDonorAsync(bookingNo, "Travelling", donor);

        Assert.True(result.Succeeded, result.Error);
    }

    /// <summary>Moves a booking's slot-day, as the passing of time would.</summary>
    private Task MoveToAsync(string bookingNo, DateOnly date) =>
        ExecuteAsync(
            """
            UPDATE booking.SlotInstances SET SlotDate = @date
            WHERE Id = (SELECT b.SlotInstanceId FROM booking.Bookings b WHERE b.BookingNo = @bookingNo);
            """,
            new { bookingNo, date = date.ToDateTime(TimeOnly.MinValue) });

    private sealed class NoLocks : ISlotLocks
    {
        public Task<ISlotLock> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ISlotLock>(new Unlocked());

        private sealed class Unlocked : ISlotLock
        {
            public SlotLockOutcome Outcome => SlotLockOutcome.Unavailable;

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

    /// <summary>The template's text as <paramref name="booking"/>'s donor should read it.</summary>
    private async Task<string> ExpectedAsync(string template, string language, string bookingNo, DateOnly date, string institution)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var body = await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM notify.Templates WHERE Code = @template AND Channel = 'SMS' AND Language = @language;",
            new { template, language }) ?? throw new InvalidOperationException($"{template} {language} is not seeded.");

        return body
            .Replace("{bookingNo}", bookingNo, StringComparison.Ordinal)
            .Replace("{institution}", institution, StringComparison.Ordinal)
            .Replace("{date}", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", "11:30", StringComparison.Ordinal);
    }

    private async Task<int> UnprocessedAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM core.OutboxMessages WHERE ProcessedAt IS NULL;");
    }

    // ------------------------------------------------------------------ the outbox texts

    [Theory]
    [InlineData("en", "Alpha Temple")]
    [InlineData("si", NameSi)]
    [InlineData("ta", NameTa)]
    public async Task ARequestAndItsApprovalAreTexted_InTheDonorsLanguage(string language, string institution)
    {
        var (donor, phone) = await DonorAsync(language);
        var date = Today.AddDays(5);
        var bookingNo = await BookAsync(donor, await SlotAsync(), date);
        await DecideAsync(bookingNo, InstitutionDecision.Approve);

        await Processor().RunAsync();

        Assert.Equal(
        [
            new Sent(await ExpectedAsync("BOOKING_REQUESTED", language, bookingNo, date, institution), $"BOOKING_REQUESTED:{bookingNo}"),
            new Sent(await ExpectedAsync("BOOKING_APPROVED", language, bookingNo, date, institution), $"BOOKING_APPROVED:{bookingNo}"),
        ],
        await SentToAsync(phone));
        Assert.Equal(0, await UnprocessedAsync());
    }

    [Fact]
    public async Task ABookingThatNeedsNoApprovalIsConfirmedAtOnce()
    {
        var (donor, phone) = await DonorAsync();
        var date = Today.AddDays(6);
        var bookingNo = await BookAsync(donor, await SlotAsync(requiresApproval: false), date);

        await Processor().RunAsync();

        Assert.Equal(
            [new Sent(await ExpectedAsync("BOOKING_CONFIRMED", "en", bookingNo, date, "Alpha Temple"), $"BOOKING_CONFIRMED:{bookingNo}")],
            await SentToAsync(phone));
    }

    [Fact]
    public async Task ARejectionAndTheInstitutionsCancellationAreTexted_WithoutTheirReasons()
    {
        var (donor, phone) = await DonorAsync();
        var slot = await SlotAsync();
        var rejected = await BookAsync(donor, slot, Today.AddDays(7));
        var cancelled = await BookAsync(donor, slot, Today.AddDays(8));

        await DecideAsync(rejected, InstitutionDecision.Reject, "Visit bit.ly/x to rebook");
        await DecideAsync(cancelled, InstitutionDecision.Cancel, "Visit bit.ly/x to rebook");

        await Processor().RunAsync();

        var sent = await SentToAsync(phone);
        Assert.Equal(
            [$"BOOKING_REQUESTED:{rejected}", $"BOOKING_REQUESTED:{cancelled}", $"BOOKING_REJECTED:{rejected}", $"BOOKING_CANCELLED:{cancelled}"],
            sent.Select(s => s.DedupKey));
        Assert.Equal(await ExpectedAsync("BOOKING_REJECTED", "en", rejected, Today.AddDays(7), "Alpha Temple"), sent[2].Body);

        // The institution typed the reason; a text from the Sethika sender id never carries it (E2).
        Assert.DoesNotContain(sent, s => s.Body.Contains("bit.ly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADonorsOwnCancellationIsNotTextedBack()
    {
        var (donor, phone) = await DonorAsync();
        var bookingNo = await BookAsync(donor, await SlotAsync(), Today.AddDays(9));
        await CancelAsDonorAsync(donor, bookingNo);

        await Processor().RunAsync();

        Assert.Equal([$"BOOKING_REQUESTED:{bookingNo}"], (await SentToAsync(phone)).Select(s => s.DedupKey));
        Assert.Equal(0, await UnprocessedAsync());
    }

    [Fact]
    public async Task ARunRepeatedAfterTheSendTextsNobodyTwice()
    {
        // As if the process died after the SMS and before the commit: the messages come round again.
        var (donor, phone) = await DonorAsync();
        await DecideAsync(await BookAsync(donor, await SlotAsync(), Today.AddDays(10)), InstitutionDecision.Approve);
        await Processor().RunAsync();

        await ExecuteAsync("UPDATE core.OutboxMessages SET ProcessedAt = NULL WHERE AggregateType = 'Booking';", new { });
        await Processor().RunAsync();

        Assert.Equal(2, (await SentToAsync(phone)).Count);
        Assert.Equal(0, await UnprocessedAsync());
    }

    // ------------------------------------------------------------------ the evening reminder

    [Fact]
    public async Task TheReminderTextsTomorrowsApprovedBookingsOnly_AndOnce()
    {
        var tomorrow = Today.AddDays(1);
        var slot = await SlotAsync();

        var (approvedDonor, approvedPhone) = await DonorAsync("si");
        var approved = await BookAsync(approvedDonor, slot, tomorrow);
        await DecideAsync(approved, InstitutionDecision.Approve);

        var (requestedDonor, requestedPhone) = await DonorAsync();
        await BookAsync(requestedDonor, slot, tomorrow);

        var (cancelledDonor, cancelledPhone) = await DonorAsync();
        var cancelled = await BookAsync(cancelledDonor, slot, tomorrow);
        await DecideAsync(cancelled, InstitutionDecision.Approve);
        await DecideAsync(cancelled, InstitutionDecision.Cancel, "Closed");

        var (laterDonor, laterPhone) = await DonorAsync();
        await DecideAsync(await BookAsync(laterDonor, await SlotAsync(), tomorrow.AddDays(1)), InstitutionDecision.Approve);

        var (todayDonor, todayPhone) = await DonorAsync();
        var todays = await BookAsync(todayDonor, await SlotAsync(), tomorrow.AddDays(2));
        await DecideAsync(todays, InstitutionDecision.Approve);
        await MoveToAsync(todays, Today);

        var (noPhoneDonor, noPhonePhone) = await DonorAsync();
        await DecideAsync(await BookAsync(noPhoneDonor, slot, tomorrow), InstitutionDecision.Approve);
        await UnconfirmPhoneAsync(noPhoneDonor);

        Assert.Equal(new ReminderRun(tomorrow, 2, 1, 1), await Reminders().SendForTomorrowAsync());
        Assert.Equal(new ReminderRun(tomorrow, 2, 1, 1), await Reminders().SendForTomorrowAsync());

        Assert.Equal(
            [new Sent(await ExpectedAsync("BOOKING_REMINDER", "si", approved, tomorrow, NameSi), $"booking-reminder:{approved}")],
            await SentToAsync(approvedPhone));

        foreach (var phone in new[] { requestedPhone, cancelledPhone, laterPhone, todayPhone, noPhonePhone })
        {
            Assert.DoesNotContain(await SentToAsync(phone), s => s.DedupKey.StartsWith("booking-reminder:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheReminderJobRunsAsThePlatform()
    {
        // Every institution's bookings, whoever the scope's request user would have been: here a
        // donor with no booking, whom Row-Level Security would show nothing.
        var (donor, phone) = await DonorAsync();
        await DecideAsync(await BookAsync(donor, await SlotAsync(), Today.AddDays(1)), InstitutionDecision.Approve);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Enabled"] = "false" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSethikaPersistence(database.ConnectionString);
        services.AddSethikaCurrentUser<StrangerRequestUser>();
        services.AddSethikaApplication();
        services.AddSethikaJobs(configuration, database.ConnectionString);
        services.Configure<SmsOptions>(_ => { });
        services.AddScoped<ISmsSender, ConsoleSmsSender>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await new BookingReminderJob(provider.GetRequiredService<IServiceScopeFactory>()).RunAsync(CancellationToken.None);

        Assert.Single(await SentToAsync(phone), s => s.DedupKey.StartsWith("booking-reminder:", StringComparison.Ordinal));
    }

    /// <summary>A signed-in person with nothing of their own: what a request scope would be.</summary>
    private sealed class StrangerRequestUser : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public int? InstitutionId => null;

        public bool IsPlatform => false;

        public string? TokenId => null;
    }
}

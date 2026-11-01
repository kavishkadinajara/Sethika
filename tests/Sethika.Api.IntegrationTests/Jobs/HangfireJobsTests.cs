using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Onboarding.Applying;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Api.IntegrationTests.Jobs;

/// <summary>
/// Plan 02, 5.7: the API host's own Hangfire server, with SQL Server storage in the test database,
/// runs the recurring outbox job and the text goes out.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class HangfireJobsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string RequestNo = "REQ-TEST-HF-0001";
    private const string ProbeEvent = "Test.Probe";

    private readonly Guid _applicant = Guid.CreateVersion7();
    private readonly string _phone = "+9475" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>What the probe saw: who the job ran as, and how many applications RLS let it read.</summary>
    private sealed class ProbeRecorder
    {
        public TaskCompletionSource<(bool IsPlatform, int Applications)> Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A handler that reads a tenant table, as a reminder job will: only the platform sees every row.</summary>
    private sealed class ProbeHandler(ICurrentUserContext user, IDbConnectionFactory connections, ProbeRecorder recorder) : IOutboxHandler
    {
        public IReadOnlyCollection<string> EventTypes => [ProbeEvent];

        public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            var applications = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM org.RegistrationRequests WHERE RequestNo IN ('REQ-ALPHA', 'REQ-BETA');");
            recorder.Seen.TrySetResult((user.IsPlatform, applications));
        }
    }

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE core.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE ProcessedAt IS NULL;
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status)
            VALUES (@applicant, 'Donor', N'Applicant', @phone, 1, 'en', 'Active');
            """,
            new { applicant = _applicant, phone = _phone });
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            DELETE FROM notify.Notifications WHERE Recipient = @phone;
            DELETE FROM core.OutboxMessages WHERE AggregateId = @RequestNo OR EventType = 'Test.Probe';
            DELETE FROM auth.Users WHERE Id = @applicant;
            """,
            new { phone = _phone, RequestNo, applicant = _applicant });
    }

    private WebApplicationFactory<Program> Host(bool? jobs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", "hangfire-jobs-test-signing-key-at-least-32-bytes");
            if (jobs is { } enabled)
            {
                builder.UseSetting("Jobs:Enabled", enabled ? "true" : "false");
            }

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ProbeRecorder>();
                services.AddScoped<IOutboxHandler, ProbeHandler>();
            });
        });

    [Fact]
    public async Task TheHostsJobServerRunsTheOutboxJob_AsThePlatform_AndTheApplicantIsTexted()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT core.OutboxMessages (EventType, AggregateType, AggregateId, Payload) VALUES (@ProbeEvent, 'HangfireTest', 'probe', N'{}');",
            new { ProbeEvent });

        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT core.OutboxMessages (EventType, AggregateType, AggregateId, Payload)
            OUTPUT inserted.Id
            VALUES (@eventType, 'RegistrationRequest', @RequestNo, @payload);
            """,
            new
            {
                eventType = ApplicationHandler.SubmittedEvent,
                RequestNo,
                payload = JsonSerializer.Serialize(new { requestNo = RequestNo, applicantUserId = _applicant }),
            });

        // Starting the host starts its job server and registers the recurring job; triggering it
        // saves waiting for the minute.
        await using var host = Host(jobs: true);
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        try
        {
            // Hangfire keeps recurring jobs in the database, so this one is this host's only because
            // the end of this test removes it - otherwise an earlier run's would answer the trigger.
            using (var storage = host.Services.GetRequiredService<JobStorage>().GetConnection())
            {
                var jobs = storage.GetRecurringJobs().OrderBy(j => j.Id, StringComparer.Ordinal).ToList();
                Assert.Equal(3, jobs.Count);

                var services = jobs[0];
                Assert.Equal((AppointmentReminderJob.RecurringJobId, "0 18 * * *", typeof(AppointmentReminderJob)), (services.Id, services.Cron, services.Job?.Type));

                var reminders = jobs[1];
                Assert.Equal((BookingReminderJob.RecurringJobId, "0 18 * * *", typeof(BookingReminderJob)), (reminders.Id, reminders.Cron, reminders.Job?.Type));

                // 18:00 in Colombo (plan 03, Q3; Phase E, V15), whatever zone the server is in.
                Assert.Equal(TimeSpan.FromHours(5.5), TimeZoneInfo.FindSystemTimeZoneById(reminders.TimeZoneId).BaseUtcOffset);
                Assert.Equal(TimeSpan.FromHours(5.5), TimeZoneInfo.FindSystemTimeZoneById(services.TimeZoneId).BaseUtcOffset);

                var outbox = jobs[2];
                Assert.Equal((OutboxJob.RecurringJobId, Cron.Minutely(), typeof(OutboxJob)), (outbox.Id, outbox.Cron, outbox.Job?.Type));
            }

            recurring.Trigger(OutboxJob.RecurringJobId);

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (await connection.ExecuteScalarAsync<DateTime?>("SELECT ProcessedAt FROM core.OutboxMessages WHERE Id = @id;", new { id }) is null)
            {
                Assert.True(DateTime.UtcNow < deadline, "The outbox job never processed the message.");
                await Task.Delay(250);
            }

            Assert.Equal(
                ($"ONBOARD_SUBMITTED:{RequestNo}:{id}", "Sent"),
                await connection.QuerySingleAsync<(string, string)>(
                    "SELECT DedupKey, Status FROM notify.Notifications WHERE Recipient = @phone;", new { phone = _phone }));

            // The same run, in the job's own scope: the platform, so Row-Level Security showed both tenants' rows.
            Assert.Equal((true, 2), await host.Services.GetRequiredService<ProbeRecorder>().Seen.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
            foreach (var job in storage.GetRecurringJobs())
            {
                recurring.RemoveIfExists(job.Id);
            }
        }
    }

    [Fact]
    public async Task ATestHostRunsNoJobs()
    {
        // JobsOffInTests turns them off for every host that does not ask.
        await using var host = Host(jobs: null);

        Assert.Null(host.Services.GetService<IRecurringJobManager>());
        Assert.DoesNotContain(
            host.Services.GetServices<IHostedService>(),
            service => service.GetType().Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) == true);
    }
}

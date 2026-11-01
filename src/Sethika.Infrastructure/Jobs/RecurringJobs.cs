using Hangfire;
using Microsoft.Extensions.Hosting;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Jobs;

/// <summary>Registers the recurring jobs when the host starts. AddOrUpdate, so a restart changes nothing.</summary>
internal sealed class RecurringJobs(IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        jobs.AddOrUpdate<OutboxJob>(
            OutboxJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely(),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        jobs.AddOrUpdate<BookingReminderJob>(
            BookingReminderJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            BookingReminderJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        jobs.AddOrUpdate<AppointmentReminderJob>(
            AppointmentReminderJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            AppointmentReminderJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

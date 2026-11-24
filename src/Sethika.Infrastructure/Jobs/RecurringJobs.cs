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

        jobs.AddOrUpdate<RecurringBookingJob>(
            RecurringBookingJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            RecurringBookingJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        jobs.AddOrUpdate<StandingBookingJob>(
            StandingBookingJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            StandingBookingJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        jobs.AddOrUpdate<MemorialReminderJob>(
            MemorialReminderJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            MemorialReminderJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        jobs.AddOrUpdate<GeoIndexRebuildJob>(
            GeoIndexRebuildJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            GeoIndexRebuildJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(SriLankaTime.ZoneId) });

        jobs.AddOrUpdate<PushReceiptsJob>(
            PushReceiptsJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            PushReceiptsJob.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // And now, so a host whose Redis was flushed or down searches through the index again within
        // moments of starting, not after the night.
        jobs.Trigger(GeoIndexRebuildJob.RecurringJobId);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

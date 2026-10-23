using Hangfire;
using Microsoft.Extensions.Hosting;

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

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

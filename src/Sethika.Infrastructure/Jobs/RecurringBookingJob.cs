using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Bookings.Recurring;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The night job that runs <see cref="RecurringBookingRun"/>, as the platform (plan 07, H3).</summary>
public sealed class RecurringBookingJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "recurring-bookings";

    /// <summary>00:30 every day, in Colombo time: just after a new day opens every slot's window one day further.</summary>
    public const string Cron = "30 0 * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>
    /// Retried, and safe to: each year is claimed in its booking's transaction, so a retry or a second
    /// run books nothing twice and texts nobody twice.
    /// </remarks>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<RecurringBookingRun>().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

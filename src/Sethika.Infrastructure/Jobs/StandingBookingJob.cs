using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Bookings.Sponsors;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The night job that runs <see cref="StandingBookingRun"/>, as the platform (plan 08, K3).</summary>
public sealed class StandingBookingJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "standing-bookings";

    /// <summary>00:45 every day, in Colombo time: after the yearly job (00:30), on the window the new day opened.</summary>
    public const string Cron = "45 0 * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>
    /// Retried, and safe to: a day is booked once, under its sponsorship's lock, so a retry or a second
    /// run books nothing twice.
    /// </remarks>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<StandingBookingRun>().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

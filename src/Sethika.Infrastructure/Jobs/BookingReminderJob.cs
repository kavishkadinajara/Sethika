using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Bookings.Notifications;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The evening job that runs <see cref="BookingReminders"/>, as the platform (plan 03, C11).</summary>
public sealed class BookingReminderJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "booking-reminders";

    /// <summary>18:00 every day (Q3), in Colombo time: the job is registered with that zone.</summary>
    public const string Cron = "0 18 * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>
    /// Retried within the half hour, so a retry still means the same "tomorrow": the run works out the
    /// day when it starts, and one retried after midnight would remind the wrong day's donors. Every
    /// reminder already sent is skipped by its dedup key.
    /// </remarks>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<BookingReminders>().SendForTomorrowAsync(cancellationToken).ConfigureAwait(false);
    }
}

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Bookings.Memorial;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The evening job that runs <see cref="MemorialReminders"/>, as the platform (plan 07, H8).</summary>
public sealed class MemorialReminderJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "memorial-reminders";

    /// <summary>18:00 every day, in Colombo time, with the booking reminders.</summary>
    public const string Cron = "0 18 * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>
    /// Retried, and safe to: every reminder already sent is skipped by its dedup key, and an
    /// observance stays in the week ahead for seven nights, so a retry after midnight misses nothing.
    /// </remarks>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<MemorialReminders>().SendAsync(cancellationToken).ConfigureAwait(false);
    }
}

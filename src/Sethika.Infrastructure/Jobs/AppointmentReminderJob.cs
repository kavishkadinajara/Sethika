using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Services.Appointments;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The evening job that runs <see cref="AppointmentReminders"/>, as the platform (Phase E, V15).</summary>
public sealed class AppointmentReminderJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "appointment-reminders";

    /// <summary>18:00 every day, in Colombo time, like the meal reminder (V15).</summary>
    public const string Cron = BookingReminderJob.Cron;

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>Retried within the half hour, so a retry still means the same "tomorrow" (see <see cref="BookingReminderJob"/>).</remarks>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<AppointmentReminders>().SendForTomorrowAsync(cancellationToken).ConfigureAwait(false);
    }
}

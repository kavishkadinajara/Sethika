using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Infrastructure.Notifications;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The job that runs <see cref="PushReceipts"/>, as the platform (plan F, F29).</summary>
public sealed class PushReceiptsJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "push-receipts";

    /// <summary>Every 15 minutes, the wait Expo advises before asking.</summary>
    public const string Cron = "*/15 * * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>Not retried: the next run is 15 minutes away, and picks up whatever this one left.</remarks>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<PushReceipts>().CheckAsync(cancellationToken).ConfigureAwait(false);
    }
}

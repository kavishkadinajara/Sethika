using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Infrastructure.Messaging;

namespace Sethika.Infrastructure.Jobs;

/// <summary>The recurring job that runs <see cref="OutboxProcessor"/>, as the platform.</summary>
public sealed class OutboxJob(IServiceScopeFactory scopes)
{
    public const string RecurringJobId = "outbox";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    /// <remarks>
    /// No Hangfire retry: the outbox counts attempts per message, and a retried run would only claim
    /// what the next minute's run claims anyway.
    /// </remarks>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

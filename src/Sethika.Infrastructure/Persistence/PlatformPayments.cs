using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Payments;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IPlatformPayments"/>: starting a gift and PayHere's notification, run in a platform scope
/// (plan 09, M8-M10).
/// </summary>
/// <remarks>
/// As <see cref="PlatformPledges"/>: the scope is new and holds nothing of the request. Row-Level Security
/// protects no one in here, so <see cref="OnlinePaymentHandler"/> is written to reach only the caller's own
/// order, and to trust a notification only after its signature.
/// </remarks>
public sealed class PlatformPayments(IServiceScopeFactory scopes) : IPlatformPayments
{
    public async Task<GiveResult> StartAsync(Guid userId, string slug, GiveInput input, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreatePlatformScope();
        var handler = scope.ServiceProvider.GetRequiredService<OnlinePaymentHandler>();
        return await handler.StartAsync(userId, slug, input, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> NotifyAsync(PayHereNotification notification, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreatePlatformScope();
        var handler = scope.ServiceProvider.GetRequiredService<OnlinePaymentHandler>();
        return await handler.NotifyAsync(notification, cancellationToken).ConfigureAwait(false);
    }
}

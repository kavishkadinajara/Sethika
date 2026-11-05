using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Needs;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IOwnPledges"/>: a donor's own pledge and cancel, run in a platform scope (plan 09, M14).
/// </summary>
/// <remarks>
/// As <see cref="PlatformOwnStandingDays"/>: the scope is new, holds nothing of the request, and is
/// given only the caller's id and the address they named. Row-Level Security does not protect anyone in
/// here, so <see cref="PledgeHandler"/>'s two methods are written to reach nothing but the caller's own.
/// </remarks>
public sealed class PlatformPledges(IServiceScopeFactory scopes) : IOwnPledges
{
    public async Task<NeedResult> PledgeAsync(Guid userId, Guid needId, PledgeInput input, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreatePlatformScope();
        var handler = scope.ServiceProvider.GetRequiredService<PledgeHandler>();
        return await handler.PledgeAsync(userId, needId, input, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NeedResult> CancelAsync(Guid userId, Guid pledgeId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreatePlatformScope();
        var handler = scope.ServiceProvider.GetRequiredService<PledgeHandler>();
        return await handler.CancelOwnAsync(userId, pledgeId, cancellationToken).ConfigureAwait(false);
    }
}

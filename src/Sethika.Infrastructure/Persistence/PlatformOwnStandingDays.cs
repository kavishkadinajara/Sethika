using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Sponsors;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// <see cref="IOwnStandingDays"/>: a linked sponsor's own skip, run in a platform scope (plan 08, K24, K35).
/// </summary>
/// <remarks>
/// The one place a request's work runs in a scope from <see cref="JobScopes.CreatePlatformScope"/>,
/// and only for this call: the scope is new, holds nothing of the request, and is given only the
/// caller's id and the booking number they named. Inside it
/// <see cref="StandingSponsorshipHandler.SkipOwnAsync"/> finds the sponsorship only through a
/// booking that is the caller's and a donor book row linked to the caller, and then lets the day go by
/// the staff skip's own rules, locks and texts (K19, K23): one judge, not a copy.
/// </remarks>
public sealed class PlatformOwnStandingDays(IServiceScopeFactory scopes) : IOwnStandingDays
{
    public async Task<SponsorshipChangeResult> SkipAsync(Guid userId, string bookingNo, string? reason, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreatePlatformScope();
        var handler = scope.ServiceProvider.GetRequiredService<StandingSponsorshipHandler>();
        return await handler.SkipOwnAsync(userId, bookingNo, reason, cancellationToken).ConfigureAwait(false);
    }
}

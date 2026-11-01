using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Jobs;

/// <summary>Rebuilds the geo index from SQL Server, as the platform (plan 06, G5): when the host starts, and every night.</summary>
/// <remarks>
/// The outbox keeps the index current; this makes it converge whatever the outbox missed, and builds
/// it on a host whose Redis was empty or down when it last ran. Until the first build the search asks
/// SQL Server, so a late or failed run is slower searches, never wrong ones.
/// </remarks>
public sealed partial class GeoIndexRebuildJob(IServiceScopeFactory scopes, ILogger<GeoIndexRebuildJob> logger)
{
    public const string RecurringJobId = "geo-index-rebuild";

    /// <summary>03:00 every day, in Colombo time: the job is registered with that zone.</summary>
    public const string Cron = "0 3 * * *";

    /// <summary>One run. Hangfire passes its shutdown token.</summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreatePlatformScope();
        var count = await scope.ServiceProvider.GetRequiredService<IInstitutionGeoIndex>().RebuildAsync(cancellationToken).ConfigureAwait(false);
        LogRebuilt(logger, count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo index rebuilt with {Count} institutions.")]
    private static partial void LogRebuilt(ILogger logger, int count);
}

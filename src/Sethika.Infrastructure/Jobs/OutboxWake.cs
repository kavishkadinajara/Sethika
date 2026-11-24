using Hangfire;
using Microsoft.Extensions.Logging;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Jobs;

/// <summary>
/// Wakes the outbox by queueing <see cref="OutboxJob"/> now (plan 07, H10). The minute's run is still
/// the guarantee: a wake that fails is logged and the change waits up to a minute, never lost.
/// </summary>
/// <remarks>
/// Two runs at once are safe: the processor claims each message before handling it. A request that
/// wakes the outbox has already committed, so a failure here must not fail it.
/// </remarks>
internal sealed partial class HangfireOutboxWake(IBackgroundJobClient jobs, ILogger<HangfireOutboxWake> logger) : IOutboxWake
{
    public void Wake()
    {
        try
        {
            jobs.Enqueue<OutboxJob>(job => job.RunAsync(CancellationToken.None));
        }
        catch (BackgroundJobClientException ex)
        {
            // Hangfire wraps every failure to store the job in this one.
            LogWakeFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not wake the outbox; the next minute's run delivers.")]
    private static partial void LogWakeFailed(ILogger logger, Exception exception);
}

/// <summary>A host with its jobs off (tests, tools) has nothing to wake.</summary>
internal sealed class NoOutboxWake : IOutboxWake
{
    public void Wake()
    {
    }
}

/// <summary>Starts <see cref="StandingBookingJob"/> now (plan 08, K3); a failure is logged and the night's run books the days.</summary>
internal sealed partial class HangfireStandingBookingWake(IBackgroundJobClient jobs, ILogger<HangfireStandingBookingWake> logger) : IStandingBookingWake
{
    public void Wake()
    {
        try
        {
            jobs.Enqueue<StandingBookingJob>(job => job.RunAsync(CancellationToken.None));
        }
        catch (BackgroundJobClientException ex)
        {
            LogWakeFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start the standing job; the night's run books the days.")]
    private static partial void LogWakeFailed(ILogger logger, Exception exception);
}

/// <summary>A host with its jobs off (tests, tools): the days wait for a run.</summary>
internal sealed class NoStandingBookingWake : IStandingBookingWake
{
    public void Wake()
    {
    }
}

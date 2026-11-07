using Microsoft.Extensions.Logging;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Writes the push to the log instead of sending it. Development only, as for
/// <see cref="ConsoleSmsSender"/>: everything around it, the delivery log, the dedup index and the
/// receipts job, runs for real. Every push is taken and every receipt is good.
/// </summary>
public sealed partial class ConsolePushGateway(ILogger<ConsolePushGateway> logger) : IPushGateway
{
    public Task<PushAnswer> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogPush(logger, message.TemplateCode, message.Title, message.Body);

        return Task.FromResult(new PushAnswer($"console-{Guid.CreateVersion7():N}", Error: null));
    }

    public Task<IReadOnlyDictionary<string, PushAnswer>> GetReceiptsAsync(
        IReadOnlyCollection<string> ticketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        return Task.FromResult<IReadOnlyDictionary<string, PushAnswer>>(
            ticketIds.ToDictionary(id => id, _ => new PushAnswer(TicketId: null, Error: null), StringComparer.Ordinal));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DEVELOPMENT push {TemplateCode}: {Title}: {Body}")]
    private static partial void LogPush(ILogger logger, string templateCode, string? title, string body);
}

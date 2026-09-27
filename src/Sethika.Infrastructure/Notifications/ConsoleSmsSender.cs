using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Notifications;

/// <summary>
/// Writes the message to the log instead of sending it. Development only.
/// </summary>
/// <remarks>
/// This is what makes signup work end to end on day one without a gateway account, at no cost, and
/// it still writes <c>notify.Notifications</c> through <see cref="SmsSenderBase"/> - so the delivery
/// log, the dedup index and the segment and cost columns are all exercised by ordinary development
/// traffic rather than waiting for a provider.
///
/// It logs the message body, which for an OTP means logging a live credential. That is acceptable in
/// development and not anywhere else, so the host refuses to start with this sender selected outside
/// the Development environment.
/// </remarks>
public sealed partial class ConsoleSmsSender(
    IDbConnectionFactory connectionFactory,
    IOptions<SmsOptions> options,
    ILogger<ConsoleSmsSender> logger) : SmsSenderBase(connectionFactory, options.Value)
{
    protected override Task<string?> DeliverAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        LogMessage(logger, phoneNumber, message);

        // A synthetic id, so the ProviderMessageId column is populated in development too and code
        // that reads it is not exercised for the first time in production.
        return Task.FromResult<string?>($"console-{Guid.CreateVersion7():N}");
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DEVELOPMENT SMS to {PhoneNumber}: {Message}")]
    private static partial void LogMessage(ILogger logger, string phoneNumber, string message);
}

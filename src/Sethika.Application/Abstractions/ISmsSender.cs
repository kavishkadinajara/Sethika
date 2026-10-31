namespace Sethika.Application.Abstractions;

/// <summary>
/// Sends a text message. One method, so nothing above Infrastructure knows which gateway - or
/// whether a gateway at all - is behind it.
/// </summary>
/// <remarks>
/// Two implementations exist: one that writes the message to the log for development, and one
/// that calls a real Sri Lankan gateway. Both record the send in <c>notify.Notifications</c>, so
/// the delivery log, the dedup key and the per-plan SMS cost columns are exercised in
/// development too and switching gateways changes one class rather than a data path.
/// </remarks>
public interface ISmsSender
{
    /// <summary>Sends <paramref name="message"/> to <paramref name="phoneNumber"/>.</summary>
    /// <param name="phoneNumber">The recipient in E.164 form.</param>
    /// <param name="message">The message body, already localised.</param>
    /// <param name="dedupKey">
    /// Optional idempotency key. When supplied, a send already recorded under the same key is not
    /// repeated, so a retried job cannot text the same person twice.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SendAsync(
        string phoneNumber,
        string message,
        string? dedupKey = null,
        CancellationToken cancellationToken = default);
}

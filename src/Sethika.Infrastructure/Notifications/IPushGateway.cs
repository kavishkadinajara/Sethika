namespace Sethika.Infrastructure.Notifications;

/// <summary>One push to one phone, already in the recipient's language.</summary>
/// <param name="Token">The Expo push token. Never written to a log (F31).</param>
/// <param name="Title">The heading, from the template's <c>Subject</c>.</param>
/// <param name="Body">The text.</param>
/// <param name="TemplateCode">Goes to the app with the push, which opens the screen it belongs to.</param>
public sealed record PushMessage(string Token, string? Title, string Body, string TemplateCode);

/// <summary>What the push service said about a push, on a ticket or on a receipt.</summary>
/// <param name="TicketId">The ticket to ask about later. Null on a refusal, and on a receipt.</param>
/// <param name="Error">Why it was refused, or null when it was taken.</param>
public sealed record PushAnswer(string? TicketId, string? Error)
{
    /// <summary>Expo's code for a token that no longer reaches a phone.</summary>
    public const string DeviceNotRegistered = "DeviceNotRegistered";

    /// <summary>Whether the token should stop being sent to.</summary>
    public bool DeviceIsGone => Error == DeviceNotRegistered;
}

/// <summary>Reaches the push service. Recording a push is <see cref="PushingUserTextMessages"/>'s work, not this.</summary>
public interface IPushGateway
{
    /// <summary>Hands over one push. A refusal of that push is an answer; a failed call throws.</summary>
    Task<PushAnswer> SendAsync(PushMessage message, CancellationToken cancellationToken = default);

    /// <summary>The receipts ready for <paramref name="ticketIds"/>. One that is not ready is left out.</summary>
    Task<IReadOnlyDictionary<string, PushAnswer>> GetReceiptsAsync(
        IReadOnlyCollection<string> ticketIds,
        CancellationToken cancellationToken = default);
}

using System.Text.Json;
using Sethika.Application.Abstractions;
using Sethika.Application.Onboarding.Applying;
using Sethika.Application.Onboarding.Approval;

namespace Sethika.Application.Onboarding.Notifications;

/// <summary>
/// Texts the applicant at the four points of an application they need to hear about (plan 02,
/// B10): received, a reviewer's question, approved, not approved.
/// </summary>
/// <remarks>
/// The message carries the request number and nothing else - no name, no reason - so a text read
/// over someone's shoulder says nothing they could not learn by signing in.
/// </remarks>
public sealed class OnboardingSmsHandler(IUserTextMessages texts) : IOutboxHandler
{
    /// <summary>Written when a reviewer asks for more information (5.4's review API).</summary>
    public const string InfoRequestedEvent = "RegistrationInfoRequested";

    /// <summary>Written when an application is rejected (5.4's review API).</summary>
    public const string RejectedEvent = "RegistrationRejected";

    private static readonly Dictionary<string, string> TemplateFor = new(StringComparer.Ordinal)
    {
        [ApplicationHandler.SubmittedEvent] = "ONBOARD_SUBMITTED",
        [InfoRequestedEvent] = "ONBOARD_INFO_REQUESTED",
        [ApproveRegistrationHandler.ApprovedEvent] = "ONBOARD_APPROVED",
        [RejectedEvent] = "ONBOARD_REJECTED",
    };

    public IReadOnlyCollection<string> EventTypes => TemplateFor.Keys;

    public async Task HandleAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var template = TemplateFor[message.EventType];
        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, JsonSerializerOptions.Web);
        if (payload is not { RequestNo: { Length: > 0 } requestNo, ApplicantUserId: var applicant } || applicant == Guid.Empty)
        {
            throw new InvalidOperationException($"Outbox message {message.Id} ({message.EventType}) has no request number or applicant.");
        }

        // Keyed by the outbox message as well as the request: a reviewer can ask more than once, and
        // each question is its own text, while a re-run of one message is not.
        await texts.SendAsync(
            applicant,
            template,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["requestNo"] = requestNo },
            $"{template}:{requestNo}:{message.Id}",
            cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(string? RequestNo, Guid ApplicantUserId);
}

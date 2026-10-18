using System.Text.Json;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;

namespace Sethika.Application.Onboarding;

/// <summary>The outbox message an application's change leaves for the applicant's SMS (plan 02, B10).</summary>
internal static class OnboardingOutbox
{
    /// <summary>Ids and codes only: the handler looks up the phone and the language when it sends.</summary>
    public static OutboxMessage Message(RegistrationRequest request, string eventType, DateTime now) => new()
    {
        EventType = eventType,
        AggregateType = "RegistrationRequest",
        AggregateId = request.RequestNo,
        Payload = JsonSerializer.Serialize(new { requestNo = request.RequestNo, applicantUserId = request.ApplicantUserId }),
        OccurredAt = now,
    };
}

using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>The limits on the application flow (plan 02, B3). Per account: an applicant is signed in.</summary>
internal static class OnboardingRateLimits
{
    /// <summary>
    /// New applications. The handler also caps open ones at three; this bounds the number taken
    /// from the request series by starting and withdrawing in a loop.
    /// </summary>
    public static readonly RateLimitRule CreatePerUser = new("application-create-user", 10, TimeSpan.FromHours(1));

    /// <summary>Uploads, each a write to file storage and a hash of up to the document type's size.</summary>
    public static readonly RateLimitRule UploadPerUser = new("application-upload-user", 60, TimeSpan.FromHours(1));
}

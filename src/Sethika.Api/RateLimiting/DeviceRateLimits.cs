using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>The limit on the phones one account registers and removes (plan F, F31).</summary>
internal static class DeviceRateLimits
{
    /// <summary>
    /// The app registers each time it starts signed in, so an hour holds far fewer of these than this.
    /// It bounds an account filling the table, each row of which is one more push per text.
    /// </summary>
    public static readonly RateLimitRule ChangePerUser = new("device-change-user", 60, TimeSpan.FromHours(1));
}

namespace Sethika.Domain.Notifications;

/// <summary>
/// A phone that can be sent a push (plan F, F4). Maps <c>notify.MobileDevices</c>. A donor's device,
/// not an institution's, so it has no <c>InstitutionId</c> and no Row-Level Security.
/// </summary>
/// <remarks>
/// A device that stops being sent to keeps its row, disabled. Its <see cref="Id"/> is part of every
/// push's dedup key (F28), so a phone that signs out and back in is the same device, and a re-run
/// cannot push to it twice.
/// </remarks>
public class MobileDevice
{
    /// <summary>The values written to <see cref="DisabledReason"/>.</summary>
    public static class DisabledReasons
    {
        /// <summary>The app asked: its switch went off, or it signed out (F31, F32).</summary>
        public const string Removed = "Removed";

        /// <summary>The owner signed out of everything (F31).</summary>
        public const string LogoutEverywhere = "LogoutEverywhere";

        /// <summary>Expo says the token no longer reaches a phone (F29).</summary>
        public const string DeviceNotRegistered = "DeviceNotRegistered";
    }

    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string ExpoPushToken { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
}

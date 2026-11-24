using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Notifications;

namespace Sethika.Application.Notifications.Devices;

/// <summary>
/// The phones a signed-in person can be pushed to (plan F, F31, F32): the app registers one when push
/// is on and removes it when push goes off or it signs out. Both are idempotent.
/// </summary>
/// <remarks>The user id comes from the token, never the request.</remarks>
public sealed partial class MobileDevicesHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    /// <summary>Not an Expo push token.</summary>
    public const string InvalidToken = "push_token_invalid";

    /// <summary>Neither <c>Android</c> nor <c>iOS</c>.</summary>
    public const string InvalidPlatform = "platform_invalid";

    /// <summary>Longer than the column.</summary>
    public const string InvalidAppVersion = "app_version_invalid";

    private const int MaxTokenLength = 200;
    private const int MaxAppVersionLength = 20;

    /// <summary>
    /// Makes <paramref name="token"/> <paramref name="userId"/>'s enabled device, moving it from
    /// whoever had it: the phone now answers to this account, so the last one to sign in on it gets
    /// its pushes. <see langword="null"/> on success, else one of the error codes.
    /// </summary>
    /// <remarks>
    /// Two first registrations at once both find no row and both insert; the unique constraint refuses
    /// the second, and it then takes the row the first made. The constraint is the guarantee.
    /// </remarks>
    public async Task<string?> RegisterAsync(
        Guid userId,
        string? token,
        string? platform,
        string? appVersion,
        CancellationToken cancellationToken = default)
    {
        if (!IsToken(token))
        {
            return InvalidToken;
        }

        if (platform is not ("Android" or "iOS"))
        {
            return InvalidPlatform;
        }

        var version = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim();
        if (version?.Length > MaxAppVersionLength)
        {
            return InvalidAppVersion;
        }

        var at = timeProvider.GetUtcNow().UtcDateTime;

        if (await TakeOverAsync(userId, token, platform, version, at, cancellationToken).ConfigureAwait(false) > 0)
        {
            return null;
        }

        db.MobileDevices.Add(new MobileDevice
        {
            UserId = userId,
            ExpoPushToken = token,
            Platform = platform,
            AppVersion = version,
            CreatedAt = at,
            LastSeenAt = at,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            db.ChangeTracker.Clear();
            await TakeOverAsync(userId, token, platform, version, at, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Stops pushes to <paramref name="token"/>, if it is <paramref name="userId"/>'s.</summary>
    /// <remarks>Nothing tells the caller whether it was, so this cannot be used to test a token.</remarks>
    public async Task RemoveAsync(Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        var at = timeProvider.GetUtcNow().UtcDateTime;

        await db.MobileDevices
            .Where(d => d.ExpoPushToken == token && d.UserId == userId && d.DisabledAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.DisabledAt, at)
                    .SetProperty(d => d.DisabledReason, MobileDevice.DisabledReasons.Removed),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<int> TakeOverAsync(Guid userId, string token, string platform, string? version, DateTime at, CancellationToken cancellationToken) =>
        db.MobileDevices
            .Where(d => d.ExpoPushToken == token)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.UserId, userId)
                    .SetProperty(d => d.Platform, platform)
                    .SetProperty(d => d.AppVersion, version)
                    .SetProperty(d => d.LastSeenAt, at)
                    .SetProperty(d => d.DisabledAt, (DateTime?)null)
                    .SetProperty(d => d.DisabledReason, (string?)null),
                cancellationToken);

    /// <summary>The two prefixes Expo's own server SDK accepts, and no more than the column holds.</summary>
    private static bool IsToken([NotNullWhen(true)] string? token) =>
        token is { Length: <= MaxTokenLength } && TokenShape().IsMatch(token);

    [GeneratedRegex(@"\AExpo(nent)?PushToken\[[^\[\]\s]+\]\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenShape();
}

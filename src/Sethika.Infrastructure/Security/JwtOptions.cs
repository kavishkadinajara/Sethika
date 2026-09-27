namespace Sethika.Infrastructure.Security;

/// <summary>Signing key and token lifetimes. Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// The HMAC secret. User Secrets locally and host configuration in Azure, never
    /// <c>appsettings.json</c>. See <see cref="SethikaJwt.MinSigningKeyBytes"/>.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long an access token is accepted.</summary>
    /// <remarks>
    /// The security stamp is checked on refresh, not on every request, so this is also the longest
    /// a revoked user keeps working: fifteen minutes on their current token. Shorter narrows that
    /// window and costs a refresh round-trip more often.
    /// </remarks>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a refresh token is accepted if it is never used.</summary>
    /// <remarks>
    /// Every use rotates it, so this bounds an idle session, not an active one: a donor who comes
    /// back within two weeks stays signed in.
    /// </remarks>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);
}

namespace Sethika.Domain.Access;

/// <summary>
/// A refresh token, stored only as its hash. Maps <c>auth.RefreshTokens</c>.
/// </summary>
/// <remarks>
/// Every token belongs to a family - the chain started by one sign-in. Rotation adds a row to the
/// family and points the old one at it; presenting a token that has already been rotated means it
/// was copied, and the whole family is revoked.
/// </remarks>
public class RefreshToken
{
    public long Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = null!;

    public Guid FamilyId { get; set; }

    /// <summary>The <c>jti</c> of the access token issued alongside this one.</summary>
    public string JwtId { get; set; } = null!;

    public int? ActiveInstitutionId { get; set; }
    public string? DeviceInfo { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public long? ReplacedByTokenId { get; set; }
}

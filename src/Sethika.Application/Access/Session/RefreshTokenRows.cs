using Sethika.Domain.Access;

namespace Sethika.Application.Access.Session;

/// <summary>
/// Builds the <c>auth.RefreshTokens</c> row for a newly issued pair - at sign-in, the first of a
/// family; at refresh, the next.
/// </summary>
public static class RefreshTokenRows
{
    /// <summary>A row for <paramref name="issued"/>, holding only the refresh token's hash.</summary>
    /// <param name="issued">The pair just minted.</param>
    /// <param name="userId">Whose session it is.</param>
    /// <param name="familyId">A new id at sign-in; the presented token's family at refresh.</param>
    /// <param name="activeInstitutionId">The institution the access token acts for.</param>
    /// <param name="securityStamp">The user's stamp now, which every later refresh must still match.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="now">When it was issued.</param>
    public static RefreshToken New(
        IssuedTokens issued,
        Guid userId,
        Guid familyId,
        int? activeInstitutionId,
        string securityStamp,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(issued);

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = issued.Refresh.TokenHash,
            FamilyId = familyId,
            JwtId = issued.Access.JwtId,
            ActiveInstitutionId = activeInstitutionId,
            SecurityStamp = securityStamp,
            DeviceInfo = Truncate(userAgent, 200),
            CreatedByIp = Truncate(ipAddress, 45),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = issued.Refresh.ExpiresAt.UtcDateTime,
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.Security;

public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public Guid? UserId =>
        Guid.TryParse(Find(SethikaClaimTypes.UserId) ?? Find(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public int? InstitutionId =>
        int.TryParse(Find(SethikaClaimTypes.InstitutionId), CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public bool IsPlatform =>
        bool.TryParse(Find(SethikaClaimTypes.IsPlatform), out var isPlatform) && isPlatform;

    public string? TokenId => Find(JwtRegisteredClaimNames.Jti);

    private string? Find(string claimType) =>
        accessor.HttpContext?.User?.FindFirst(claimType)?.Value;
}

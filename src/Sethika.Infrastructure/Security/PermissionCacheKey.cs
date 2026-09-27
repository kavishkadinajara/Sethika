using System.Globalization;

namespace Sethika.Infrastructure.Security;

public static class PermissionCacheKey
{
    public const string GlobalScope = "global";

    public static string For(Guid userId, int? institutionId, int userVersion, int institutionVersion)
    {
        var scope = institutionId?.ToString(CultureInfo.InvariantCulture) ?? GlobalScope;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"perm:{userId:D}:{scope}:v{userVersion}.{institutionVersion}");
    }
}

using Microsoft.AspNetCore.Authorization;

namespace Sethika.Api.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    internal const string PolicyPrefix = "sethika:perm:";

    public HasPermissionAttribute(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        PermissionCode = permissionCode;
        Policy = PolicyPrefix + permissionCode;
    }

    public string PermissionCode { get; }
}

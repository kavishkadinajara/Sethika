using Microsoft.AspNetCore.Authorization;
using Sethika.Application.Abstractions;

namespace Sethika.Api.Security;

public sealed class PermissionAuthorizationHandler(
    ICurrentUserContext currentUser,
    IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // Anonymous callers hold nothing. No point querying.
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        var effective = await permissions
            .GetEffectivePermissionsAsync(userId, currentUser.InstitutionId)
            .ConfigureAwait(false);

        if (effective.Allows(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
    }
}

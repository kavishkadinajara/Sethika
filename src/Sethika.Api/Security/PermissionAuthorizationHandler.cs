using Microsoft.AspNetCore.Authorization;
using Sethika.Application.Abstractions;

namespace Sethika.Api.Security;

public sealed class PermissionAuthorizationHandler(
    ICurrentUserContext currentUser,
    IPermissionService permissions,
    IStepUpGrants stepUps) : AuthorizationHandler<PermissionRequirement>
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

        if (!effective.Allows(requirement.PermissionCode))
        {
            return;
        }

        // Holding a permission and being allowed to exercise it right now are different questions
        // (plan A5). The refusal is marked so the client is told to step up, not merely refused.
        if (effective.RequiresMfa(requirement.PermissionCode)
            && !await HasSteppedUpAsync(userId).ConfigureAwait(false))
        {
            StepUpAuthorizationResultHandler.MarkStepUpRequired(context.Resource);
            return;
        }

        context.Succeed(requirement);
    }

    /// <summary>A token without a <c>jti</c> cannot hold a grant, so it has not stepped up.</summary>
    private async Task<bool> HasSteppedUpAsync(Guid userId) =>
        currentUser.TokenId is { } tokenId
        && await stepUps.IsGrantedAsync(userId, tokenId).ConfigureAwait(false);
}

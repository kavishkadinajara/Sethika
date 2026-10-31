using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Roles;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Custom roles, made and handed out inside an institution - plan 5.9. Every endpoint acts for the
/// institution in the caller's token, never one named in the request.
/// </summary>
/// <remarks>
/// <c>[HasPermission]</c> decides who may reach each endpoint, from the cached permission set. The
/// handler then checks rank, ceiling and holding again inside its transaction, against the
/// database: the cache says whether to try, the transaction says whether it happens.
/// </remarks>
internal static class InstitutionRoleEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution");

        institution.MapGet("/role-levels", GetRoleLevelsAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.ROLES.VIEW"));

        // The access screens' reads (plan 04, X6).
        institution.MapGet("/roles", GetRolesAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.ROLES.VIEW"));

        institution.MapGet("/permission-catalogue", GetCatalogueAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.ROLES.VIEW"));

        institution.MapGet("/members", GetMembersAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.VIEW"));

        institution.MapGet("/invitations", GetInvitationsAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.VIEW"));

        // Staff invitations (plan 04, X1-X5): the same permission as handing out a role.
        institution.MapPost("/invitations", InviteAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.EDIT"));

        institution.MapPost("/invitations/{id:guid}/cancel", CancelInvitationAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.EDIT"));

        institution.MapPost("/roles", CreateAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.ROLES.CREATE"));

        institution.MapPut("/roles/{code}/permissions", SetPermissionsAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.ROLES.EDIT"));

        institution.MapPost("/roles/{code}/members", AssignAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.EDIT"));

        institution.MapDelete("/roles/{code}/members/{user:guid}", RevokeAsync)
            .WithMetadata(new HasPermissionAttribute("ACCESS.MEMBERS.EDIT"));

        return app;
    }

    private static async Task<IResult> GetRoleLevelsAsync(
        ICurrentUserContext currentUser,
        IRoleLevelQueries queries,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var levels = await queries.GetGrantableAsync(actor.UserId, actor.InstitutionId, cancellationToken);
        return Results.Ok(levels.Select(l => new { level = l.Level, name = l.Name, rank = l.Rank, permissions = l.Permissions }));
    }

    private static async Task<IResult> GetRolesAsync(
        ICurrentUserContext currentUser,
        IAccessQueries queries,
        CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.GetRolesAsync(actor.UserId, actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> GetCatalogueAsync(
        ICurrentUserContext currentUser,
        IAccessQueries queries,
        CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.GetCatalogueAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> GetMembersAsync(
        ICurrentUserContext currentUser,
        IAccessQueries queries,
        CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.GetMembersAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> GetInvitationsAsync(
        ICurrentUserContext currentUser,
        IAccessQueries queries,
        CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.GetPendingInvitationsAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> InviteAsync(
        [FromBody] InviteBody body,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await roles.InviteAsync(actor, new InviteCommand(body.Phone, body.Role), cancellationToken);
        return result.Succeeded
            ? Results.Json(new { id = result.Invitation }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> CancelInvitationAsync(
        Guid id,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await roles.CancelInvitationAsync(actor, id, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateRoleBody body,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await roles.CreateAsync(
            actor,
            new CreateRoleCommand(body.Code, body.Name, body.Level, body.Permissions),
            cancellationToken);

        if (!result.Succeeded)
        {
            return Refused(result);
        }

        return Results.Json(
            new RoleResponse(
                body.Code!.Trim().ToUpperInvariant(),
                body.Name!.Trim(),
                body.Level!.Trim().ToUpperInvariant(),
                [.. (body.Permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal)]),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> SetPermissionsAsync(
        string code,
        [FromBody] RolePermissionsBody body,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await roles.SetPermissionsAsync(actor, code, body.Permissions, body.Version, cancellationToken);
        return result.Succeeded ? Results.Ok(new { version = result.Version }) : Refused(result);
    }

    private static async Task<IResult> AssignAsync(
        string code,
        [FromBody] RoleMemberBody body,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (body.User is not { } userId)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "user_required");
        }

        var result = await roles.AssignAsync(actor, code, userId, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> RevokeAsync(
        string code,
        Guid user,
        ICurrentUserContext currentUser,
        RoleManagementHandler roles,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await roles.RevokeAsync(actor, code, user, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static RoleActor? Actor(ICurrentUserContext currentUser) =>
        currentUser is { UserId: { } userId, InstitutionId: { } institutionId } ? new RoleActor(userId, institutionId) : null;

    /// <summary>
    /// Unreachable through <c>[HasPermission]</c>, since institution permissions are only effective
    /// in an institution, but the handler is not run on that assumption.
    /// </summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    /// <summary>A refused role change as the API answers it; also used by the invitee's endpoints in <see cref="MeEndpoints"/>.</summary>
    internal static IResult Refused(RoleChangeResult result) => result.Failure switch
    {
        RoleChangeFailure.InvalidCode => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_role_code"),
        RoleChangeFailure.InvalidName => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_role_name"),
        RoleChangeFailure.UnknownLevel => ApiErrors.Error(StatusCodes.Status400BadRequest, "unknown_level"),
        RoleChangeFailure.UnknownPermission => WithPermissions(StatusCodes.Status400BadRequest, "unknown_permission", result),
        RoleChangeFailure.RoleExists => ApiErrors.Error(StatusCodes.Status409Conflict, "role_exists"),
        RoleChangeFailure.RoleNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "role_not_found"),
        RoleChangeFailure.MemberNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "member_not_found"),
        RoleChangeFailure.AssignmentNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "assignment_not_found"),
        RoleChangeFailure.NoRank => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_rank"),
        RoleChangeFailure.RankNotJunior => ApiErrors.Error(StatusCodes.Status403Forbidden, "rank_not_junior"),
        RoleChangeFailure.OutsideCeiling => WithPermissions(StatusCodes.Status403Forbidden, "outside_ceiling", result),
        RoleChangeFailure.NotHeld => WithPermissions(StatusCodes.Status403Forbidden, "not_held", result),
        RoleChangeFailure.OwnRoles => ApiErrors.Error(StatusCodes.Status403Forbidden, "own_roles"),
        RoleChangeFailure.InvalidPhone => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number"),
        RoleChangeFailure.AccountNotFound => ApiErrors.Error(StatusCodes.Status409Conflict, "account_not_found"),
        RoleChangeFailure.AlreadyMember => ApiErrors.Error(StatusCodes.Status409Conflict, "already_member"),
        RoleChangeFailure.InvitationPending => ApiErrors.Error(StatusCodes.Status409Conflict, "invitation_pending"),
        RoleChangeFailure.InvitationNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "invitation_not_found"),
        RoleChangeFailure.InvitationClosed => ApiErrors.Error(StatusCodes.Status409Conflict, "invitation_closed"),
        RoleChangeFailure.InvitationExpired => ApiErrors.Error(StatusCodes.Status409Conflict, "invitation_expired"),
        RoleChangeFailure.LastOwner => ApiErrors.Error(StatusCodes.Status409Conflict, "last_owner"),
        RoleChangeFailure.Conflict => ApiErrors.Error(StatusCodes.Status409Conflict, "changed_meanwhile"),
        RoleChangeFailure.InvalidVersion => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_version"),
        _ => ApiErrors.Error(StatusCodes.Status400BadRequest, "role_change_refused"),
    };

    /// <summary>The refusal names the permissions it is about, so the editor can mark them.</summary>
    private static IResult WithPermissions(int statusCode, string code, RoleChangeResult result) =>
        Results.Json(new { code, permissions = result.Permissions }, statusCode: statusCode);
}

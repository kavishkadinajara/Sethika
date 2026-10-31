using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Platform;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The platform's own screens (plan 04, U4): every institution and the way into one as support,
/// every account, accounts made for people who cannot sign up, suspension, platform roles.
/// </summary>
/// <remarks>
/// Platform permissions are effective only in a personal session (as a donor's are), so an
/// administrator inside an institution as support reaches none of these until they leave it.
/// </remarks>
internal static class AdminEndpoints
{
    /// <summary>A page the 3-column grid fills (plan 05, X4), and the most one request may ask for.</summary>
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;

    /// <summary>What the list filters accept: the CHECK constraints' own values.</summary>
    private static readonly HashSet<string> InstitutionStatuses = new(StringComparer.Ordinal) { "Onboarding", "Active", "Suspended", "Deactivated" };
    private static readonly HashSet<string> UserKinds = new(StringComparer.Ordinal) { "Donor", "InstitutionStaff", "PlatformStaff" };
    private static readonly HashSet<string> UserStatuses = new(StringComparer.Ordinal) { "Active", "PendingVerification", "Suspended", "Deactivated" };

    /// <summary>A type or district code: the lookups' own shape, so nothing else reaches the query.</summary>
    private const int MaxCodeLength = 30;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin");

        admin.MapGet("/institutions", GetInstitutionsAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.INSTITUTIONS.VIEW"));

        // The names behind the type and district codes the filters take, under the list's own
        // permission, as the reviewer's lookups are under theirs.
        admin.MapGet("/institutions/lookups", OnboardingApplicationEndpoints.GetLookupsAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.INSTITUTIONS.VIEW"));

        // The platform dashboard's "institutions by status" (plan 05, R4).
        admin.MapGet("/institutions/statuses", async (IAdminQueries queries, CancellationToken ct) => Results.Ok(await queries.GetInstitutionStatusesAsync(ct)))
            .WithMetadata(new HasPermissionAttribute("PLATFORM.INSTITUTIONS.VIEW"));

        // Y1a: RequiresMfa, so the step-up middleware asks before a ticket is issued.
        admin.MapPost("/institutions/{slug}/support", IssueSupportTicketAsync)
            .WithMetadata(new HasPermissionAttribute(PlatformSupport.Permission));

        admin.MapGet("/users", GetUsersAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.VIEW"));

        admin.MapPost("/users", CreateUserAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.CREATE"));

        admin.MapPost("/users/{id:guid}/suspend", (Guid id, ICurrentUserContext user, PlatformAdminHandler handler, CancellationToken ct) =>
                SetSuspendedAsync(id, suspend: true, user, handler, ct))
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.EDIT"));

        admin.MapPost("/users/{id:guid}/reactivate", (Guid id, ICurrentUserContext user, PlatformAdminHandler handler, CancellationToken ct) =>
                SetSuspendedAsync(id, suspend: false, user, handler, ct))
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.EDIT"));

        admin.MapGet("/platform-roles", GetPlatformRolesAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.VIEW"));

        admin.MapPost("/users/{id:guid}/platform-roles", GrantPlatformRoleAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.EDIT"));

        admin.MapDelete("/users/{id:guid}/platform-roles/{role}", RevokePlatformRoleAsync)
            .WithMetadata(new HasPermissionAttribute("PLATFORM.USERS.EDIT"));

        return app;
    }

    private static async Task<IResult> GetInstitutionsAsync(
        string? search,
        string? status,
        string? type,
        string? district,
        string? sort,
        int? page,
        int? pageSize,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (Blank(status) is { } s && !InstitutionStatuses.Contains(s)
            || Blank(type) is { Length: > MaxCodeLength }
            || Blank(district) is { Length: > MaxCodeLength }
            || Blank(sort) is { } o && o is not ("name" or "newest"))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_filter");
        }

        if (page is < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_page");
        }

        return Results.Ok(await queries.GetInstitutionsAsync(
            new AdminInstitutionFilter(search, Blank(status), Blank(type), Blank(district), Blank(sort) == "newest", page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    private static async Task<IResult> GetUsersAsync(
        string? search,
        string? kind,
        string? status,
        string? sort,
        int? page,
        int? pageSize,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (Blank(kind) is { } k && !UserKinds.Contains(k)
            || Blank(status) is { } s && !UserStatuses.Contains(s)
            || Blank(sort) is { } o && o is not ("newest" or "name"))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_filter");
        }

        if (page is < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_page");
        }

        return Results.Ok(await queries.GetUsersAsync(
            new AdminUserFilter(search, Blank(kind), Blank(status), Blank(sort) == "name", page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    /// <summary>A query value, trimmed; null when it is missing or empty, so an empty filter does not narrow.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> GetPlatformRolesAsync(ICurrentUserContext currentUser, IAdminQueries queries, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId ? Results.Ok(await queries.GetPlatformRolesAsync(userId, cancellationToken)) : Results.Unauthorized();

    private static async Task<IResult> IssueSupportTicketAsync(
        string slug,
        ICurrentUserContext currentUser,
        PlatformAdminHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.IssueSupportTicketAsync(userId, slug, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateUserBody body,
        ICurrentUserContext currentUser,
        PlatformAdminHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.CreateUserAsync(userId, body.FullName, body.Phone, body.Language, cancellationToken);
        return result.Succeeded ? Results.Json(new { id = result.Created }, statusCode: StatusCodes.Status201Created) : Refused(result);
    }

    private static async Task<IResult> SetSuspendedAsync(
        Guid id,
        bool suspend,
        ICurrentUserContext currentUser,
        PlatformAdminHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.SetSuspendedAsync(userId, id, suspend, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> GrantPlatformRoleAsync(
        Guid id,
        [FromBody] PlatformRoleBody body,
        ICurrentUserContext currentUser,
        PlatformAdminHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.GrantPlatformRoleAsync(userId, id, body.Role, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> RevokePlatformRoleAsync(
        Guid id,
        string role,
        ICurrentUserContext currentUser,
        PlatformAdminHandler handler,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.RevokePlatformRoleAsync(userId, id, role, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static IResult Refused(PlatformChangeResult result) => result.Failure switch
    {
        PlatformChangeFailure.InvalidFullName => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_full_name"),
        PlatformChangeFailure.InvalidPhoneNumber => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_phone_number"),
        PlatformChangeFailure.InvalidLanguage => ApiErrors.Error(StatusCodes.Status400BadRequest, "unsupported_language"),
        PlatformChangeFailure.AlreadyRegistered => ApiErrors.Error(StatusCodes.Status409Conflict, "already_registered"),
        PlatformChangeFailure.UserNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "user_not_found"),
        PlatformChangeFailure.RoleNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "role_not_found"),
        PlatformChangeFailure.InstitutionNotFound => ApiErrors.Error(StatusCodes.Status404NotFound, "institution_not_found"),
        PlatformChangeFailure.OwnAccount => ApiErrors.Error(StatusCodes.Status403Forbidden, "own_account"),
        PlatformChangeFailure.RankNotJunior => ApiErrors.Error(StatusCodes.Status403Forbidden, "rank_not_junior"),
        PlatformChangeFailure.Unavailable => ApiErrors.Error(StatusCodes.Status503ServiceUnavailable, "unavailable"),
        _ => ApiErrors.Error(StatusCodes.Status400BadRequest, "platform_change_refused"),
    };
}

/// <summary>An account made by the platform (plan 04, Y6).</summary>
public sealed record CreateUserBody(string? FullName, string? Phone, string? Language);

/// <summary>A platform role to give, by code.</summary>
public sealed record PlatformRoleBody(string? Role);

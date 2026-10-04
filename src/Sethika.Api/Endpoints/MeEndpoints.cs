using Sethika.Application.Abstractions;

namespace Sethika.Api.Endpoints;

/// <summary>The signed-in user's view of their own account.</summary>
/// <remarks>
/// Authenticated but not permission-gated: everything under <c>/api/me</c> reads or changes only
/// the caller's own row, named by the token, never by the request. A permission to see one's own
/// permissions would be circular, and no role should be needed to choose one's own language.
/// </remarks>
internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("/permissions", GetPermissionsAsync);

        return app;
    }

    /// <summary>
    /// The caller's own effective permission codes.
    /// </summary>
    /// <remarks>
    /// The Next.js app reads this once per session to decide what to render; it is a convenience
    /// for the UI, not a security boundary - each endpoint still carries its own [HasPermission].
    /// </remarks>
    private static async Task<IResult> GetPermissionsAsync(
        ICurrentUserContext currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var effective = await permissions
            .GetEffectivePermissionsAsync(userId, currentUser.InstitutionId, cancellationToken);

        return Results.Ok(new
        {
            userId,
            institutionId = currentUser.InstitutionId,
            isPlatform = currentUser.IsPlatform,
            permissions = effective.Codes.OrderBy(c => c, StringComparer.Ordinal),
        });
    }
}

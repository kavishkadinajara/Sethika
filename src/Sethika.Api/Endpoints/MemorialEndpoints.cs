using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Memorial;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The Memorial Dana Planner (plan 07, H6): the faiths and their observances, and the donor's plans
/// with the days they fall on. A booking for an observance goes through <c>POST /api/bookings</c>
/// with <c>memorialPlan</c> and <c>memorialRule</c>.
/// </summary>
/// <remarks>
/// The DONOR role's permissions, which apply only in a personal session (plan 02, B1), like My
/// bookings. A plan is named by its public id; one that is not the donor's, or archived, is 404.
/// </remarks>
internal static class MemorialEndpoints
{
    private const string PlansView = "MYMEMORIAL.PLANS.VIEW";
    private const string PlansCreate = "MYMEMORIAL.PLANS.CREATE";
    private const string PlansEdit = "MYMEMORIAL.PLANS.EDIT";

    private static readonly string[] DateOfPassingField = ["dateOfPassing"];

    public static IEndpointRouteBuilder MapMemorialEndpoints(this IEndpointRouteBuilder app)
    {
        var memorial = app.MapGroup("/api/memorial");

        memorial.MapGet("/religions", ListReligionsAsync).WithMetadata(new HasPermissionAttribute(PlansView));
        memorial.MapGet("/relationships", ListRelationshipsAsync).WithMetadata(new HasPermissionAttribute(PlansView));

        memorial.MapGet("/plans", ListPlansAsync).WithMetadata(new HasPermissionAttribute(PlansView));
        memorial.MapPost("/plans", CreatePlanAsync)
            .WithMetadata(new HasPermissionAttribute(PlansCreate))
            .RateLimitPerUser(BookingRateLimits.MemorialPlanPerUser);
        memorial.MapGet("/plans/{publicId:guid}", GetPlanAsync).WithMetadata(new HasPermissionAttribute(PlansView));
        memorial.MapPost("/plans/{publicId:guid}/archive", ArchivePlanAsync).WithMetadata(new HasPermissionAttribute(PlansEdit));

        return app;
    }

    private static async Task<IResult> ListReligionsAsync(IMemorialQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListReligionsAsync(cancellationToken));

    private static async Task<IResult> ListRelationshipsAsync(IMemorialQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListRelationshipsAsync(cancellationToken));

    private static async Task<IResult> ListPlansAsync(ICurrentUserContext currentUser, MemorialPlanner planner, CancellationToken cancellationToken) =>
        Results.Ok(await planner.ListAsync(currentUser.UserId!.Value, cancellationToken));

    private static async Task<IResult> GetPlanAsync(Guid publicId, ICurrentUserContext currentUser, MemorialPlanner planner, CancellationToken cancellationToken) =>
        await planner.GetAsync(publicId, currentUser.UserId!.Value, cancellationToken) is { } plan
            ? Results.Ok(plan)
            : ApiErrors.Error(StatusCodes.Status404NotFound, MemorialPlanResult.NotFound);

    private static async Task<IResult> CreatePlanAsync([FromBody] MemorialPlanBody body, ICurrentUserContext currentUser, MemorialPlanHandler handler, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(body.DateOfPassing?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOfPassing))
        {
            return Results.Json(new { code = MemorialPlanResult.InvalidDetails, fields = DateOfPassingField }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.CreateAsync(
            currentUser.UserId!.Value,
            new CreateMemorialPlanCommand(body.DeceasedName, body.Relationship, dateOfPassing, body.Religion, body.Notes),
            cancellationToken);

        return result.Succeeded
            ? Results.Json(new { plan = result.Plan }, statusCode: StatusCodes.Status201Created)
            : Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> ArchivePlanAsync(Guid publicId, ICurrentUserContext currentUser, MemorialPlanHandler handler, CancellationToken cancellationToken) =>
        await handler.ArchiveAsync(publicId, currentUser.UserId!.Value, cancellationToken) is { } error
            ? ApiErrors.Error(StatusCodes.Status404NotFound, error)
            : Results.NoContent();
}

/// <summary>The planner's form.</summary>
/// <param name="DateOfPassing"><c>yyyy-MM-dd</c>.</param>
/// <param name="Religion">A religion's code from <c>GET /api/memorial/religions</c>.</param>
/// <param name="Relationship">A relationship's code from <c>GET /api/memorial/relationships</c>, or none.</param>
internal sealed record MemorialPlanBody(string? DeceasedName, string? Relationship, string? DateOfPassing, string? Religion, string? Notes);

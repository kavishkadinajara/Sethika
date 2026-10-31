using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Services.Setup;
using Sethika.Domain.Services;

namespace Sethika.Api.Endpoints;

/// <summary>
/// What an institution sets up for religious services (Phase E, 8.4): the services it offers, its
/// leaders, their usual week and their leave, and the leaders it may assign to one appointment.
/// Every endpoint acts for the institution in the caller's token, never one named in the request.
/// </summary>
/// <remarks>
/// Service types and leaders are addressed by <c>PublicId</c>, appointments by <c>AppointmentNo</c>.
/// Leave has only its internal id, which is tenant-internal and never reaches a donor. Anything of
/// another institution answers 404, exactly like something that does not exist.
/// </remarks>
internal static class ServiceSetupEndpoints
{
    private const string TypesView = "SERVICES.TYPES.VIEW";
    private const string TypesCreate = "SERVICES.TYPES.CREATE";
    private const string TypesEdit = "SERVICES.TYPES.EDIT";
    private const string LeadersView = "SERVICES.LEADERS.VIEW";
    private const string LeadersCreate = "SERVICES.LEADERS.CREATE";
    private const string LeadersEdit = "SERVICES.LEADERS.EDIT";
    private const string AppointmentsApprove = "SERVICES.APPOINTMENTS.APPROVE";

    public static IEndpointRouteBuilder MapServiceSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution");

        institution.MapGet("/service-types", ListTypesAsync).WithMetadata(new HasPermissionAttribute(TypesView));
        institution.MapPost("/service-types", CreateTypeAsync).WithMetadata(new HasPermissionAttribute(TypesCreate));
        institution.MapPut("/service-types/{publicId:guid}", UpdateTypeAsync).WithMetadata(new HasPermissionAttribute(TypesEdit));
        institution.MapPost("/service-types/{publicId:guid}/deactivate", (Guid publicId, ICurrentUserContext user, ServiceSetupHandler handler, CancellationToken ct) =>
            SetTypeActiveAsync(publicId, active: false, user, handler, ct)).WithMetadata(new HasPermissionAttribute(TypesEdit));
        institution.MapPost("/service-types/{publicId:guid}/activate", (Guid publicId, ICurrentUserContext user, ServiceSetupHandler handler, CancellationToken ct) =>
            SetTypeActiveAsync(publicId, active: true, user, handler, ct)).WithMetadata(new HasPermissionAttribute(TypesEdit));

        institution.MapGet("/leaders", ListLeadersAsync).WithMetadata(new HasPermissionAttribute(LeadersView));
        institution.MapPost("/leaders", CreateLeaderAsync).WithMetadata(new HasPermissionAttribute(LeadersCreate));
        institution.MapPut("/leaders/{publicId:guid}", UpdateLeaderAsync).WithMetadata(new HasPermissionAttribute(LeadersEdit));
        institution.MapPost("/leaders/{publicId:guid}/deactivate", (Guid publicId, ICurrentUserContext user, ServiceSetupHandler handler, CancellationToken ct) =>
            SetLeaderActiveAsync(publicId, active: false, user, handler, ct)).WithMetadata(new HasPermissionAttribute(LeadersEdit));
        institution.MapPost("/leaders/{publicId:guid}/activate", (Guid publicId, ICurrentUserContext user, ServiceSetupHandler handler, CancellationToken ct) =>
            SetLeaderActiveAsync(publicId, active: true, user, handler, ct)).WithMetadata(new HasPermissionAttribute(LeadersEdit));
        institution.MapPut("/leaders/{publicId:guid}/weekly-hours", SetWeeklyHoursAsync).WithMetadata(new HasPermissionAttribute(LeadersEdit));

        institution.MapGet("/leaders/time-off", ListTimeOffAsync).WithMetadata(new HasPermissionAttribute(LeadersView));
        institution.MapPost("/leaders/{publicId:guid}/time-off", AddTimeOffAsync).WithMetadata(new HasPermissionAttribute(LeadersEdit));
        institution.MapDelete("/leaders/time-off/{id:int}", RemoveTimeOffAsync).WithMetadata(new HasPermissionAttribute(LeadersEdit));

        institution.MapGet("/appointments/{appointmentNo}/candidates", CandidatesAsync).WithMetadata(new HasPermissionAttribute(AppointmentsApprove));

        return app;
    }

    // ------------------------------------------------------------------ service types

    private static async Task<IResult> ListTypesAsync(ICurrentUserContext currentUser, IServiceSetupQueries queries, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ListTypesAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> CreateTypeAsync([FromBody] ServiceTypeInput body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.CreateTypeAsync(actor.InstitutionId, actor.UserId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId, version = result.Version }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateTypeAsync(Guid publicId, [FromBody] ServiceTypeInput body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.UpdateTypeAsync(actor.InstitutionId, actor.UserId, publicId, body, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    private static async Task<IResult> SetTypeActiveAsync(Guid publicId, bool active, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.SetTypeActiveAsync(actor.InstitutionId, actor.UserId, publicId, active, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    // ------------------------------------------------------------------ leaders

    private static async Task<IResult> ListLeadersAsync(ICurrentUserContext currentUser, IServiceSetupQueries queries, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ListLeadersAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> CreateLeaderAsync([FromBody] LeaderInput body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.CreateLeaderAsync(actor.InstitutionId, actor.UserId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId, version = result.Version }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateLeaderAsync(Guid publicId, [FromBody] LeaderInput body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.UpdateLeaderAsync(actor.InstitutionId, actor.UserId, publicId, body, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    private static async Task<IResult> SetLeaderActiveAsync(Guid publicId, bool active, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.SetLeaderActiveAsync(actor.InstitutionId, actor.UserId, publicId, active, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { publicId = result.PublicId, version = result.Version, appointments = result.Appointments })
            : Refused(result);
    }

    private static async Task<IResult> SetWeeklyHoursAsync(Guid publicId, [FromBody] WeeklyHoursBody body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.SetWeeklyHoursAsync(actor.InstitutionId, publicId, body.Spans, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    // ------------------------------------------------------------------ leave

    private static async Task<IResult> ListTimeOffAsync(ICurrentUserContext currentUser, IServiceSetupQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ListTimeOffAsync(actor.InstitutionId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> AddTimeOffAsync(Guid publicId, [FromBody] TimeOffInput body, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.AddTimeOffAsync(actor.InstitutionId, actor.UserId, publicId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { id = result.TimeOffId }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> RemoveTimeOffAsync(int id, ICurrentUserContext currentUser, ServiceSetupHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.RemoveTimeOffAsync(actor.InstitutionId, id, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    // ------------------------------------------------------------------ the assign sheet

    private static async Task<IResult> CandidatesAsync(string appointmentNo, ICurrentUserContext currentUser, IServiceSetupQueries queries, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        return await queries.LeaderCandidatesAsync(actor.InstitutionId, appointmentNo, cancellationToken) is { } candidates
            ? Results.Ok(candidates)
            : ApiErrors.Error(StatusCodes.Status404NotFound, "appointment_not_found");
    }

    // ------------------------------------------------------------------ plumbing

    private readonly record struct Acting(Guid UserId, int InstitutionId);

    private static Acting? Actor(ICurrentUserContext currentUser) =>
        currentUser is { UserId: { } userId, InstitutionId: { } institutionId } ? new Acting(userId, institutionId) : null;

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(SetupChangeResult result)
    {
        var status = result.Error switch
        {
            SetupChangeResult.InvalidDetails => StatusCodes.Status400BadRequest,
            SetupChangeResult.ServiceTypeNotFound or SetupChangeResult.LeaderNotFound or SetupChangeResult.TimeOffNotFound
                => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        if (result.Fields.Count > 0)
        {
            return Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status);
        }

        return result.Error == ServiceErrors.TimeOffClashes
            ? Results.Json(new { code = result.Error, appointments = result.Appointments }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

/// <summary>A leader's whole usual week; it replaces the one recorded.</summary>
internal sealed record WeeklyHoursBody(IReadOnlyList<WeeklyHoursInput>? Spans);

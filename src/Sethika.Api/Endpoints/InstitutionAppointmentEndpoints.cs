using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Services;

namespace Sethika.Api.Endpoints;

/// <summary>
/// What an institution does with service appointments (Phase E, 8.7): the agenda, assigning and
/// removing leaders, answering for a leader, rejecting, cancelling and completing; and a signed-in
/// leader's own schedule and answers. Every endpoint acts for the institution in the caller's token.
/// </summary>
/// <remarks>
/// Appointments are addressed by <c>AppointmentNo</c>, leaders by <c>PublicId</c>. Anything of another
/// institution answers 404, exactly like something that does not exist.
/// </remarks>
internal static class InstitutionAppointmentEndpoints
{
    private const string AppointmentsView = "SERVICES.APPOINTMENTS.VIEW";
    private const string AppointmentsApprove = "SERVICES.APPOINTMENTS.APPROVE";
    private const string AppointmentsReject = "SERVICES.APPOINTMENTS.REJECT";
    private const string AppointmentsEdit = "SERVICES.APPOINTMENTS.EDIT";
    private const string ScheduleView = "SERVICES.MYSCHEDULE.VIEW";
    private const string ScheduleEdit = "SERVICES.MYSCHEDULE.EDIT";

    private static readonly Dictionary<string, AppointmentAgenda> Agendas = new(StringComparer.Ordinal)
    {
        ["upcoming"] = AppointmentAgenda.Upcoming,
        ["waiting"] = AppointmentAgenda.Waiting,
        ["needs-leaders"] = AppointmentAgenda.NeedsLeaders,
        ["history"] = AppointmentAgenda.History,
    };

    /// <summary>Refusals the caller fixes by changing what they sent.</summary>
    private static readonly HashSet<string> BadRequest = new(StringComparer.Ordinal)
    {
        AppointmentResult.InvalidDetails,
        ServiceErrors.ReasonRequired,
        ServiceErrors.ReasonTooLong,
    };

    public static IEndpointRouteBuilder MapInstitutionAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution");

        institution.MapGet("/appointments", AgendaAsync).WithMetadata(new HasPermissionAttribute(AppointmentsView));
        institution.MapPost("/appointments/{appointmentNo}/leaders", AssignAsync).WithMetadata(new HasPermissionAttribute(AppointmentsApprove));
        institution.MapDelete("/appointments/{appointmentNo}/leaders/{leader:guid}", RemoveAsync).WithMetadata(new HasPermissionAttribute(AppointmentsEdit));
        institution.MapPost("/appointments/{appointmentNo}/leaders/{leader:guid}/respond", RespondOnBehalfAsync).WithMetadata(new HasPermissionAttribute(AppointmentsEdit));

        institution.MapPost("/appointments/{appointmentNo}/reject", ([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body, string appointmentNo, ICurrentUserContext user, AppointmentDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(appointmentNo, AppointmentDecision.Reject, body?.Reason, user, handler, ct)).WithMetadata(new HasPermissionAttribute(AppointmentsReject));
        institution.MapPost("/appointments/{appointmentNo}/cancel", ([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body, string appointmentNo, ICurrentUserContext user, AppointmentDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(appointmentNo, AppointmentDecision.Cancel, body?.Reason, user, handler, ct)).WithMetadata(new HasPermissionAttribute(AppointmentsEdit));
        institution.MapPost("/appointments/{appointmentNo}/complete", (string appointmentNo, ICurrentUserContext user, AppointmentDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(appointmentNo, AppointmentDecision.Complete, null, user, handler, ct)).WithMetadata(new HasPermissionAttribute(AppointmentsEdit));

        institution.MapGet("/schedule", ScheduleAsync).WithMetadata(new HasPermissionAttribute(ScheduleView));
        institution.MapPost("/schedule/{appointmentNo}/respond", RespondAsLeaderAsync).WithMetadata(new HasPermissionAttribute(ScheduleEdit));

        return app;
    }

    // ------------------------------------------------------------------ the agenda

    private static async Task<IResult> AgendaAsync(string? view, ICurrentUserContext currentUser, IAppointmentQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (!Agendas.TryGetValue(string.IsNullOrWhiteSpace(view) ? "upcoming" : view.Trim(), out var agenda))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_view");
        }

        return Results.Ok(await queries.AgendaAsync(actor.InstitutionId, agenda, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }

    // ------------------------------------------------------------------ leaders

    private static async Task<IResult> AssignAsync(string appointmentNo, [FromBody] AssignBody body, ICurrentUserContext currentUser, AssignLeadersHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.AssignAsync(actor.InstitutionId, appointmentNo, body.Leaders ?? [], actor.UserId, cancellationToken);
        return Answer(appointmentNo, result);
    }

    private static async Task<IResult> RemoveAsync(string appointmentNo, Guid leader, ICurrentUserContext currentUser, AppointmentDecisionHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        return Answer(appointmentNo, await handler.RemoveLeaderAsync(actor.InstitutionId, appointmentNo, leader, actor.UserId, cancellationToken));
    }

    private static async Task<IResult> RespondOnBehalfAsync(string appointmentNo, Guid leader, [FromBody] RespondBody body, ICurrentUserContext currentUser, AppointmentDecisionHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (body.Accept is not { } accept)
        {
            return AcceptMissing();
        }

        return Answer(appointmentNo, await handler.RespondOnBehalfAsync(actor.InstitutionId, appointmentNo, leader, accept, body.Reason, actor.UserId, cancellationToken));
    }

    // ------------------------------------------------------------------ decisions

    private static async Task<IResult> DecideAsync(string appointmentNo, AppointmentDecision decision, string? reason, ICurrentUserContext currentUser, AppointmentDecisionHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        return Answer(appointmentNo, await handler.DecideAsync(actor.InstitutionId, appointmentNo, decision, reason, actor.UserId, cancellationToken));
    }

    // ------------------------------------------------------------------ a leader's own schedule

    private static async Task<IResult> ScheduleAsync(ICurrentUserContext currentUser, IAppointmentQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ScheduleAsync(actor.InstitutionId, actor.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> RespondAsLeaderAsync(string appointmentNo, [FromBody] RespondBody body, ICurrentUserContext currentUser, AppointmentDecisionHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (body.Accept is not { } accept)
        {
            return AcceptMissing();
        }

        return Answer(appointmentNo, await handler.RespondAsLeaderAsync(actor.InstitutionId, appointmentNo, accept, body.Reason, actor.UserId, cancellationToken));
    }

    // ------------------------------------------------------------------ plumbing

    private readonly record struct Acting(Guid UserId, int InstitutionId);

    private static Acting? Actor(ICurrentUserContext currentUser) =>
        currentUser is { UserId: { } userId, InstitutionId: { } institutionId } ? new Acting(userId, institutionId) : null;

    private static readonly string[] AcceptField = ["accept"];

    private static IResult AcceptMissing() =>
        Results.Json(new { code = AppointmentResult.InvalidDetails, fields = AcceptField }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Answer(string appointmentNo, AppointmentResult result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(new { appointmentNo = appointmentNo.Trim(), status = result.Status });
        }

        var status = result.Error switch
        {
            AppointmentResult.NotFound => StatusCodes.Status404NotFound,
            AppointmentResult.NotALeader => StatusCodes.Status403Forbidden,
            _ when BadRequest.Contains(result.Error!) => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Leaders.Count > 0
            ? Results.Json(new { code = result.Error, leaders = result.Leaders }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

/// <summary>The leaders to assign, by <c>PublicId</c>.</summary>
internal sealed record AssignBody(IReadOnlyList<Guid>? Leaders);

/// <summary>A leader's answer to an assignment; a reason only for a decline.</summary>
internal sealed record RespondBody(bool? Accept, string? Reason);

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Services;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The donor's side of religious services (Phase E, 8.5): asking for one, listing them, and
/// cancelling. The services an institution offers are on its public page,
/// <c>GET /api/institutions/{slug}</c>.
/// </summary>
/// <remarks>
/// The endpoints hold the DONOR role's <c>MYSERVICES.APPOINTMENTS.*</c> permissions, which apply
/// only in a personal session (plan 02, B1), like booking a meal.
/// </remarks>
internal static class DonorAppointmentEndpoints
{
    private const string MineView = "MYSERVICES.APPOINTMENTS.VIEW";
    private const string MineCreate = "MYSERVICES.APPOINTMENTS.CREATE";
    private const string MineEdit = "MYSERVICES.APPOINTMENTS.EDIT";

    /// <summary>Refusals the donor fixes by changing what they sent; the rest are about the service, the time or the account.</summary>
    private static readonly HashSet<string> BadRequest = new(StringComparer.Ordinal)
    {
        RequestServiceResult.InvalidDetails,
        ServiceErrors.NotOnTheGrid,
        ServiceErrors.AddressRequired,
        ServiceErrors.ReasonTooLong,
    };

    public static IEndpointRouteBuilder MapDonorAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var appointments = app.MapGroup("/api/appointments");

        appointments.MapPost("/", RequestAsync)
            .WithMetadata(new HasPermissionAttribute(MineCreate))
            .RateLimitPerUser(BookingRateLimits.RequestServicePerUser);

        appointments.MapGet("/mine", ListMineAsync).WithMetadata(new HasPermissionAttribute(MineView));
        appointments.MapPost("/{appointmentNo}/cancel", CancelAsync).WithMetadata(new HasPermissionAttribute(MineEdit));

        return app;
    }

    private static async Task<IResult> RequestAsync(
        [FromBody] AppointmentRequestBody body,
        ICurrentUserContext currentUser,
        RequestServiceHandler handler,
        CancellationToken cancellationToken)
    {
        var invalid = new List<string>();
        if (body.ServiceType is not { } serviceType || serviceType == Guid.Empty)
        {
            invalid.Add("serviceType");
        }

        if (!DateOnly.TryParseExact(body.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            invalid.Add("date");
        }

        if (!TimeOnly.TryParseExact(body.StartTime?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
        {
            invalid.Add("startTime");
        }

        if (invalid.Count > 0)
        {
            return Results.Json(new { code = RequestServiceResult.InvalidDetails, fields = invalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.RequestAsync(
            currentUser.UserId!.Value,
            new RequestServiceCommand(body.ServiceType!.Value, date, startTime, body.Location, body.Address, body.Occasion, body.Note),
            cancellationToken);

        if (result.Succeeded)
        {
            return Results.Json(new { appointmentNo = result.AppointmentNo, status = result.Status }, statusCode: StatusCodes.Status201Created);
        }

        if (result.Fields.Count > 0)
        {
            return Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest);
        }

        return ApiErrors.Error(StatusOf(result.Error!, RequestServiceResult.ServiceNotFound), result.Error!);
    }

    private static async Task<IResult> ListMineAsync(
        ICurrentUserContext currentUser,
        IDonorAppointmentQueries queries,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListMineAsync(currentUser.UserId!.Value, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));

    private static async Task<IResult> CancelAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body,
        string appointmentNo,
        ICurrentUserContext currentUser,
        AppointmentDecisionHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.CancelAsDonorAsync(appointmentNo, body?.Reason, currentUser.UserId!.Value, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { appointmentNo = appointmentNo.Trim(), status = result.Status })
            : ApiErrors.Error(StatusOf(result.Error!, AppointmentResult.NotFound), result.Error!);
    }

    private static int StatusOf(string error, string notFound) =>
        error == notFound ? StatusCodes.Status404NotFound
        : BadRequest.Contains(error) ? StatusCodes.Status400BadRequest
        : StatusCodes.Status409Conflict;
}

/// <summary>The service request form.</summary>
/// <param name="ServiceType">The service type's <c>PublicId</c>.</param>
/// <param name="Date"><c>yyyy-MM-dd</c>, the day in Sri Lanka.</param>
/// <param name="StartTime"><c>HH:mm</c>, Colombo time, on the 15-minute grid.</param>
/// <param name="Location"><c>AtInstitution</c> or <c>AtDonorPlace</c>.</param>
internal sealed record AppointmentRequestBody(
    Guid? ServiceType,
    string? Date,
    string? StartTime,
    string? Location,
    string? Address,
    string? Occasion,
    string? Note);

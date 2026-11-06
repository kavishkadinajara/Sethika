using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Slots;
using Sethika.Application.Bookings.Today;
using Sethika.Domain.Bookings;

namespace Sethika.Api.Endpoints;

/// <summary>
/// An institution's side of booking (plan 03, 5.4): its meal slots, blocked dates and listing, and
/// the calendar where it decides on bookings. Every endpoint acts for the institution in the
/// caller's token, never one named in the request.
/// </summary>
/// <remarks>
/// Slots are addressed by <c>PublicId</c> and bookings by <c>BookingNo</c>. A blocked date has only
/// its internal id, which is tenant-internal and never reaches a donor (C9). Anything of another
/// institution answers 404, exactly like something that does not exist.
/// </remarks>
internal static class InstitutionBookingEndpoints
{
    private const string SlotsView = "BOOKINGS.SLOTS.VIEW";
    private const string SlotsCreate = "BOOKINGS.SLOTS.CREATE";
    private const string SlotsEdit = "BOOKINGS.SLOTS.EDIT";
    private const string CalendarView = "BOOKINGS.CALENDAR.VIEW";
    private const string CalendarApprove = "BOOKINGS.CALENDAR.APPROVE";
    private const string CalendarReject = "BOOKINGS.CALENDAR.REJECT";
    private const string CalendarEdit = "BOOKINGS.CALENDAR.EDIT";

    /// <summary>The widest calendar one request reads: a month view with the days either side.</summary>
    public const int MaxCalendarDays = 62;

    /// <summary>How many changes the dashboard's ledger shows.</summary>
    public const int LedgerLength = 10;

    private static readonly string[] Statuses =
    [
        BookingStatus.Requested, BookingStatus.Approved, BookingStatus.Rejected,
        BookingStatus.Cancelled, BookingStatus.Completed, BookingStatus.NoShow,
    ];

    private static readonly string[] ListedField = ["listed"];

    public static IEndpointRouteBuilder MapInstitutionBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var institution = app.MapGroup("/api/institution");

        // A change below writes outbox messages - texts, the search index, the live calendars - and
        // wakes the outbox when it succeeds (plan 07, H10).
        institution.MapGet("/meal-slots", ListSlotsAsync).WithMetadata(new HasPermissionAttribute(SlotsView));
        institution.MapPost("/meal-slots", CreateSlotAsync).WithMetadata(new HasPermissionAttribute(SlotsCreate)).WakesOutbox();
        institution.MapPut("/meal-slots/{publicId:guid}", UpdateSlotAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();
        institution.MapPost("/meal-slots/{publicId:guid}/deactivate", DeactivateSlotAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();
        institution.MapPost("/meal-slots/{publicId:guid}/activate", ActivateSlotAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();

        institution.MapGet("/blocked-dates", ListBlockedDatesAsync).WithMetadata(new HasPermissionAttribute(SlotsView));
        institution.MapPost("/blocked-dates", AddBlockedDateAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();
        institution.MapDelete("/blocked-dates/{id:int}", RemoveBlockedDateAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();

        institution.MapGet("/listing", GetListingAsync).WithMetadata(new HasPermissionAttribute(SlotsView));
        institution.MapPut("/listing", SetListingAsync).WithMetadata(new HasPermissionAttribute(SlotsEdit)).WakesOutbox();

        institution.MapGet("/bookings", GetCalendarAsync).WithMetadata(new HasPermissionAttribute(CalendarView));
        institution.MapGet("/today", GetTodayAsync).WithMetadata(new HasPermissionAttribute(CalendarView));
        institution.MapGet("/ledger", GetLedgerAsync).WithMetadata(new HasPermissionAttribute(CalendarView));
        institution.MapGet("/bookings/{bookingNo}", FindBookingAsync).WithMetadata(new HasPermissionAttribute(CalendarView));
        institution.MapGet("/bookings/{bookingNo}/shared-with", ListSharedWithAsync).WithMetadata(new HasPermissionAttribute(CalendarView));

        institution.MapPost("/bookings/{bookingNo}/approve", (string bookingNo, ICurrentUserContext user, BookingDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(bookingNo, InstitutionDecision.Approve, null, user, handler, ct)).WithMetadata(new HasPermissionAttribute(CalendarApprove)).WakesOutbox();

        institution.MapPost("/bookings/{bookingNo}/reject", ([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body, string bookingNo, ICurrentUserContext user, BookingDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(bookingNo, InstitutionDecision.Reject, body?.Reason, user, handler, ct)).WithMetadata(new HasPermissionAttribute(CalendarReject)).WakesOutbox();

        institution.MapPost("/bookings/{bookingNo}/cancel", ([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body, string bookingNo, ICurrentUserContext user, BookingDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(bookingNo, InstitutionDecision.Cancel, body?.Reason, user, handler, ct)).WithMetadata(new HasPermissionAttribute(CalendarEdit)).WakesOutbox();

        institution.MapPost("/bookings/{bookingNo}/complete", (string bookingNo, ICurrentUserContext user, BookingDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(bookingNo, InstitutionDecision.Complete, null, user, handler, ct)).WithMetadata(new HasPermissionAttribute(CalendarEdit));

        institution.MapPost("/bookings/{bookingNo}/no-show", (string bookingNo, ICurrentUserContext user, BookingDecisionHandler handler, CancellationToken ct) =>
            DecideAsync(bookingNo, InstitutionDecision.NoShow, null, user, handler, ct)).WithMetadata(new HasPermissionAttribute(CalendarEdit));

        return app;
    }

    // ------------------------------------------------------------------ meal slots

    private static async Task<IResult> ListSlotsAsync(ICurrentUserContext currentUser, IInstitutionBookingQueries queries, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ListSlotsAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> CreateSlotAsync([FromBody] MealSlotInput body, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.CreateAsync(actor.InstitutionId, actor.UserId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { publicId = result.PublicId, version = result.Version }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> UpdateSlotAsync(Guid publicId, [FromBody] MealSlotInput body, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.UpdateAsync(actor.InstitutionId, actor.UserId, publicId, body, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    private static Task<IResult> DeactivateSlotAsync(Guid publicId, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken) =>
        SetActiveAsync(publicId, active: false, currentUser, handler, cancellationToken);

    private static Task<IResult> ActivateSlotAsync(Guid publicId, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken) =>
        SetActiveAsync(publicId, active: true, currentUser, handler, cancellationToken);

    private static async Task<IResult> SetActiveAsync(Guid publicId, bool active, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.SetActiveAsync(actor.InstitutionId, actor.UserId, publicId, active, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { publicId = result.PublicId, version = result.Version, bookings = result.AffectedBookings })
            : Refused(result);
    }

    // ------------------------------------------------------------------ blocked dates and listing

    private static async Task<IResult> ListBlockedDatesAsync(ICurrentUserContext currentUser, IInstitutionBookingQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.ListBlockedDatesAsync(actor.InstitutionId, SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime), cancellationToken))
            : NoInstitution();

    private static async Task<IResult> AddBlockedDateAsync([FromBody] BlockedDateInput body, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.AddBlockedDateAsync(actor.InstitutionId, actor.UserId, body, cancellationToken);
        return result.Succeeded
            ? Results.Json(new { id = result.BlockedDateId, bookings = result.AffectedBookings, standingDays = result.StandingDays }, statusCode: StatusCodes.Status201Created)
            : Refused(result);
    }

    private static async Task<IResult> RemoveBlockedDateAsync(int id, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.RemoveBlockedDateAsync(actor.InstitutionId, id, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> GetListingAsync(ICurrentUserContext currentUser, IInstitutionBookingQueries queries, CancellationToken cancellationToken) =>
        Actor(currentUser) is { } actor
            ? Results.Ok(await queries.GetListingAsync(actor.InstitutionId, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> SetListingAsync([FromBody] ListingBody body, ICurrentUserContext currentUser, SlotManagementHandler handler, CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (body.Listed is not { } listed)
        {
            return Refused(new SlotChangeResult(SlotChangeResult.InvalidDetails, ListedField, null, null, null, []));
        }

        var result = await handler.SetListedAsync(actor.InstitutionId, actor.UserId, listed, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    // ------------------------------------------------------------------ the calendar

    private static async Task<IResult> GetCalendarAsync(
        string? from,
        string? to,
        string? status,
        ICurrentUserContext currentUser,
        IInstitutionBookingQueries queries,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        if (Date(from) is not { } start || Date(to) is not { } end || end < start || end.DayNumber - start.DayNumber >= MaxCalendarDays)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_range");
        }

        var wanted = string.IsNullOrWhiteSpace(status) ? null : Array.Find(Statuses, s => s == status.Trim());
        if (!string.IsNullOrWhiteSpace(status) && wanted is null)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_status");
        }

        return Results.Ok(await queries.GetCalendarAsync(actor.InstitutionId, start, end, wanted, cancellationToken));
    }

    /// <summary>Today's meals in Colombo, booked or not (plan 05, R2): the sidebar's table and the dashboard's timeline.</summary>
    private static async Task<IResult> GetTodayAsync(
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        TodaysTable table,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await table.GetAsync(actor.InstitutionId, cancellationToken));
    }

    /// <summary>The dashboard's ledger (plan 05, R4): the latest changes of the institution's bookings.</summary>
    private static async Task<IResult> GetLedgerAsync(
        HttpContext httpContext,
        ICurrentUserContext currentUser,
        IInstitutionBookingQueries queries,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await queries.GetLedgerAsync(actor.InstitutionId, LedgerLength, cancellationToken));
    }

    /// <summary>A booking typed whole into the jump-to palette (plan 05, R2): the day to open the calendar on.</summary>
    private static async Task<IResult> FindBookingAsync(
        string bookingNo,
        ICurrentUserContext currentUser,
        IInstitutionBookingQueries queries,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        // The database's collation is case-insensitive, so "bk-2026-000123" finds BK-2026-000123 as typed.
        return await queries.FindBookingAsync(actor.InstitutionId, bookingNo.Trim(), cancellationToken) is { } place
            ? Results.Ok(place)
            : ApiErrors.Error(StatusCodes.Status404NotFound, BookingDecisionResult.NotFound);
    }

    /// <summary>Who joined the booking as a co-sponsor (plan 07, H19): names only.</summary>
    private static async Task<IResult> ListSharedWithAsync(
        string bookingNo,
        ICurrentUserContext currentUser,
        ISharingQueries queries,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        return await queries.ListSharedWithAsync(actor.InstitutionId, bookingNo.Trim(), cancellationToken) is { } names
            ? Results.Ok(names)
            : ApiErrors.Error(StatusCodes.Status404NotFound, BookingDecisionResult.NotFound);
    }

    private static async Task<IResult> DecideAsync(
        string bookingNo,
        InstitutionDecision decision,
        string? reason,
        ICurrentUserContext currentUser,
        BookingDecisionHandler handler,
        CancellationToken cancellationToken)
    {
        if (Actor(currentUser) is not { } actor)
        {
            return NoInstitution();
        }

        var result = await handler.DecideAsync(actor.InstitutionId, bookingNo, decision, reason, actor.UserId, cancellationToken);
        if (result.Succeeded)
        {
            return Results.Ok(new { bookingNo = bookingNo.Trim(), status = result.Status });
        }

        var statusCode = result.Error switch
        {
            BookingDecisionResult.NotFound => StatusCodes.Status404NotFound,
            BookingErrors.ReasonRequired or BookingErrors.ReasonTooLong => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict,
        };

        return ApiErrors.Error(statusCode, result.Error!);
    }

    // ------------------------------------------------------------------ plumbing

    private readonly record struct Acting(Guid UserId, int InstitutionId);

    private static Acting? Actor(ICurrentUserContext currentUser) =>
        currentUser is { UserId: { } userId, InstitutionId: { } institutionId } ? new Acting(userId, institutionId) : null;

    /// <summary>
    /// Unreachable through <c>[HasPermission]</c>, since institution permissions are only effective
    /// in an institution, but the handlers are not run on that assumption.
    /// </summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(SlotChangeResult result)
    {
        var status = result.Error switch
        {
            SlotChangeResult.InvalidDetails => StatusCodes.Status400BadRequest,
            SlotChangeResult.SlotNotFound or SlotChangeResult.BlockedDateNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }

    /// <summary><c>yyyy-MM-dd</c>; anything else is refused, not guessed.</summary>
    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

/// <summary>Why a booking is rejected or cancelled; the donor reads it.</summary>
internal sealed record ReasonBody(string? Reason);

/// <summary>Whether the institution appears in the public list.</summary>
internal sealed record ListingBody(bool? Listed);

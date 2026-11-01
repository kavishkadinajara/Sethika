using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Discovery;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Recurring;
using Sethika.Domain.Bookings;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The donor's side of booking (plan 03, 5.5): finding an institution and its open days, which
/// anyone may read, and booking, listing and cancelling, which a signed-in donor does.
/// </summary>
/// <remarks>
/// The institution reads are anonymous (C10), like <c>/api/legal/current</c>, and limited per client
/// address. An institution that is not <c>Active</c> answers 404, listed or not. The booking
/// endpoints hold the DONOR role's permissions, which apply only in a personal session (plan 02,
/// B1): a session switched into an institution is refused, and the web switches back first.
/// </remarks>
internal static class DonorBookingEndpoints
{
    private const string MineView = "MYBOOKINGS.BOOKINGS.VIEW";
    private const string MineCreate = "MYBOOKINGS.BOOKINGS.CREATE";
    private const string MineEdit = "MYBOOKINGS.BOOKINGS.EDIT";

    private const string InstitutionNotFound = "institution_not_found";

    /// <summary><c>org.Institutions.Slug</c>'s length; anything longer cannot be one.</summary>
    private const int MaxSlugLength = 150;

    /// <summary>Refusals the donor fixes by changing what they sent; the rest are about the slot-day or the account.</summary>
    private static readonly HashSet<string> BadRequest = new(StringComparer.Ordinal)
    {
        BookMealResult.InvalidDetails,
        BookingErrors.HeadcountOutOfRange,
        BookingErrors.FulfilmentNotOffered,
        BookingErrors.ReasonTooLong,
        BookMealResult.NotAnObservance,
    };

    public static IEndpointRouteBuilder MapDonorBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var institutions = app.MapGroup("/api/institutions").AllowAnonymous();

        institutions.MapGet("/", ListInstitutionsAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);
        institutions.MapGet("/{slug}", GetInstitutionAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);
        institutions.MapGet("/{slug}/availability", GetAvailabilityAsync).RateLimitPerClient(BookingRateLimits.PublicReadPerClient);

        var bookings = app.MapGroup("/api/bookings");

        bookings.MapPost("/", BookAsync)
            .WithMetadata(new HasPermissionAttribute(MineCreate))
            .RateLimitPerUser(BookingRateLimits.CreatePerUser);

        bookings.MapGet("/mine", ListMineAsync).WithMetadata(new HasPermissionAttribute(MineView));
        bookings.MapPost("/{bookingNo}/cancel", CancelAsync).WithMetadata(new HasPermissionAttribute(MineEdit));

        // Yearly bookings (plan 07, H5): the donor's list, and stopping one.
        bookings.MapGet("/recurring", ListYearlyAsync).WithMetadata(new HasPermissionAttribute(MineView));
        bookings.MapPost("/recurring/{publicId:guid}/stop", StopYearlyAsync).WithMetadata(new HasPermissionAttribute(MineEdit));

        return app;
    }

    // ------------------------------------------------------------------ institutions, anonymous

    private static async Task<IResult> ListInstitutionsAsync(IDonorBookingQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListInstitutionsAsync(cancellationToken));

    private static async Task<IResult> GetInstitutionAsync(string slug, IDonorBookingQueries queries, CancellationToken cancellationToken) =>
        IsSlug(slug) && await queries.GetInstitutionAsync(slug, cancellationToken) is { } institution
            ? Results.Ok(institution)
            : ApiErrors.Error(StatusCodes.Status404NotFound, InstitutionNotFound);

    private static async Task<IResult> GetAvailabilityAsync(string slug, string? month, AvailabilityCalendar calendar, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_month");
        }

        return IsSlug(slug) && await calendar.GetAsync(slug, first, cancellationToken) is { } availability
            ? Results.Ok(availability)
            : ApiErrors.Error(StatusCodes.Status404NotFound, InstitutionNotFound);
    }

    // ------------------------------------------------------------------ the donor's bookings

    private static async Task<IResult> BookAsync([FromBody] BookingBody body, ICurrentUserContext currentUser, BookMealHandler handler, CancellationToken cancellationToken)
    {
        var invalid = new List<string>();
        if (body.MealSlot is not { } mealSlot || mealSlot == Guid.Empty)
        {
            invalid.Add("mealSlot");
        }

        if (!DateOnly.TryParseExact(body.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            invalid.Add("date");
        }

        if (body.Headcount is null)
        {
            invalid.Add("headcount");
        }

        if (invalid.Count > 0)
        {
            return Results.Json(new { code = BookMealResult.InvalidDetails, fields = invalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.BookAsync(
            currentUser.UserId!.Value,
            new BookMealCommand(
                body.MealSlot!.Value, date, body.Headcount!.Value, body.Fulfilment, body.InMemoryOf, body.Notes, body.EveryYear ?? false,
                body.MemorialPlan, body.MemorialRule),
            cancellationToken);

        if (result.Succeeded)
        {
            return Results.Json(new { bookingNo = result.BookingNo, status = result.Status }, statusCode: StatusCodes.Status201Created);
        }

        if (result.Fields.Count > 0)
        {
            return Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest);
        }

        return result.Error == BookMealResult.MemorialNotFound
            ? ApiErrors.Error(StatusCodes.Status404NotFound, result.Error)
            : ApiErrors.Error(StatusOf(result.Error!, BookMealResult.SlotNotFound), result.Error!);
    }

    private static async Task<IResult> ListMineAsync(ICurrentUserContext currentUser, IDonorBookingQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListMineAsync(currentUser.UserId!.Value, SriLankaTime.Today(timeProvider.GetUtcNow().UtcDateTime), cancellationToken));

    private static async Task<IResult> CancelAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonBody? body,
        string bookingNo,
        ICurrentUserContext currentUser,
        BookingDecisionHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.CancelAsDonorAsync(bookingNo, body?.Reason, currentUser.UserId!.Value, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { bookingNo = bookingNo.Trim(), status = result.Status })
            : ApiErrors.Error(StatusOf(result.Error!, BookingDecisionResult.NotFound), result.Error!);
    }

    private static async Task<IResult> ListYearlyAsync(ICurrentUserContext currentUser, IDonorBookingQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListYearlyAsync(currentUser.UserId!.Value, cancellationToken));

    private static async Task<IResult> StopYearlyAsync(Guid publicId, ICurrentUserContext currentUser, RecurringBookingHandler handler, CancellationToken cancellationToken) =>
        await handler.StopAsync(publicId, currentUser.UserId!.Value, cancellationToken) is { } error
            ? ApiErrors.Error(StatusCodes.Status404NotFound, error)
            : Results.NoContent();

    // ------------------------------------------------------------------ plumbing

    private static int StatusOf(string error, string notFound) =>
        error == notFound ? StatusCodes.Status404NotFound
        : BadRequest.Contains(error) ? StatusCodes.Status400BadRequest
        : StatusCodes.Status409Conflict;

    private static bool IsSlug(string slug) => !string.IsNullOrWhiteSpace(slug) && slug.Trim().Length <= MaxSlugLength;
}

/// <summary>The booking form.</summary>
/// <param name="MealSlot">The slot's <c>PublicId</c>.</param>
/// <param name="Date"><c>yyyy-MM-dd</c>, the slot-day in Sri Lanka.</param>
/// <param name="Fulfilment">A <c>FulfilmentMode</c> the slot offers.</param>
/// <param name="EveryYear">Book the same meal on the same day every year from next year on (plan 07, H5).</param>
/// <param name="MemorialPlan">A memorial plan's public id, when the booking is for one of its observances (H6).</param>
/// <param name="MemorialRule">That observance's rule code, from the plan's observances.</param>
internal sealed record BookingBody(
    Guid? MealSlot,
    string? Date,
    short? Headcount,
    string? Fulfilment,
    string? InMemoryOf,
    string? Notes,
    bool? EveryYear,
    Guid? MemorialPlan,
    string? MemorialRule);

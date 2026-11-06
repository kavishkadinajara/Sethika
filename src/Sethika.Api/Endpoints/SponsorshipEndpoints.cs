using Microsoft.AspNetCore.Mvc;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Sponsors;

namespace Sethika.Api.Endpoints;

/// <summary>
/// The institution's standing sponsorships (plan 08, K2-K5, K8, K9): set one up for a donor in its donor
/// book, read it with its next days, end it, skip a day, swap two days. Every endpoint acts for the institution in
/// the caller's token, never one named in the request.
/// </summary>
/// <remarks>
/// A sponsorship is addressed by <c>PublicId</c>. Another institution's answers 404, exactly like one
/// that does not exist.
/// </remarks>
internal static class SponsorshipEndpoints
{
    private const string SponsorsView = "BOOKINGS.SPONSORS.VIEW";
    private const string SponsorsCreate = "BOOKINGS.SPONSORS.CREATE";
    private const string SponsorsEdit = "BOOKINGS.SPONSORS.EDIT";

    /// <summary>The body of ending a sponsorship: the version the screen read, and an optional reason for its cancelled days.</summary>
    internal sealed record EndBody(byte[]? Version, string? Reason);

    /// <summary>The body of skipping a day: the day, and an optional reason.</summary>
    internal sealed record SkipBody(DateOnly? Date, string? Reason);

    /// <remarks>The changes wake the outbox: each writes the hint that opens or fills calendar days (K12).</remarks>
    public static IEndpointRouteBuilder MapSponsorshipEndpoints(this IEndpointRouteBuilder app)
    {
        var sponsorships = app.MapGroup("/api/institution/sponsorships");

        sponsorships.MapGet("/", ListAsync).WithMetadata(new HasPermissionAttribute(SponsorsView));
        sponsorships.MapPost("/", CreateAsync).WithMetadata(new HasPermissionAttribute(SponsorsCreate)).WakesOutbox();
        sponsorships.MapPost("/preview", PreviewAsync).WithMetadata(new HasPermissionAttribute(SponsorsCreate));
        sponsorships.MapGet("/{publicId:guid}", GetAsync).WithMetadata(new HasPermissionAttribute(SponsorsView));
        sponsorships.MapPost("/{publicId:guid}/end", EndAsync).WithMetadata(new HasPermissionAttribute(SponsorsEdit)).WakesOutbox();
        sponsorships.MapPost("/{publicId:guid}/skip", SkipAsync).WithMetadata(new HasPermissionAttribute(SponsorsEdit)).WakesOutbox();
        sponsorships.MapPost("/swap", SwapAsync).WithMetadata(new HasPermissionAttribute(SponsorsEdit)).WakesOutbox();

        return app;
    }

    private static async Task<IResult> ListAsync([FromQuery] Guid? donor, ICurrentUserContext currentUser, IStandingSponsorshipQueries queries, CancellationToken cancellationToken) =>
        currentUser.InstitutionId is { } institutionId
            ? Results.Ok(await queries.ListAsync(institutionId, donor, cancellationToken))
            : NoInstitution();

    private static async Task<IResult> GetAsync(Guid publicId, ICurrentUserContext currentUser, StandingSponsorshipReader reader, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        return await reader.GetAsync(institutionId, publicId, cancellationToken) is { } details
            ? Results.Ok(details)
            : ApiErrors.Error(StatusCodes.Status404NotFound, SponsorshipChangeResult.NotFound);
    }

    private static async Task<IResult> CreateAsync([FromBody] SponsorshipInput body, ICurrentUserContext currentUser, StandingSponsorshipHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.CreateAsync(institutionId, userId, body, cancellationToken);
        if (!result.Succeeded)
        {
            return Refused(result);
        }

        var days = result.Days.Select(d => new { date = d.Date, outcome = OutcomeName(d.Outcome), error = d.Error, bookingNo = d.BookingNo });
        return Results.Json(new { publicId = result.PublicId, version = result.Version, days, later = result.Later }, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>The next days a sponsorship would have, and their clashes, before it is saved (K9). Writes nothing.</summary>
    private static async Task<IResult> PreviewAsync([FromBody] SponsorshipInput body, ICurrentUserContext currentUser, StandingSponsorshipPreview preview, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        var result = await preview.PreviewAsync(institutionId, body, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { days = result.Days })
            : Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> EndAsync(Guid publicId, [FromBody] EndBody body, ICurrentUserContext currentUser, StandingSponsorshipHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.EndAsync(institutionId, userId, publicId, body.Version, body.Reason, cancellationToken);
        return result.Succeeded ? Results.Ok(new { publicId = result.PublicId, version = result.Version }) : Refused(result);
    }

    private static async Task<IResult> SkipAsync(Guid publicId, [FromBody] SkipBody body, ICurrentUserContext currentUser, StandingSponsorshipHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.SkipAsync(institutionId, userId, publicId, body.Date, body.Reason, cancellationToken);
        return result.Succeeded ? Results.NoContent() : Refused(result);
    }

    private static async Task<IResult> SwapAsync([FromBody] SwapInput body, ICurrentUserContext currentUser, StandingSwapHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.SwapAsync(institutionId, userId, body, cancellationToken);
        if (result.Succeeded)
        {
            return Results.Ok(new { a = result.NewA, b = result.NewB });
        }

        var status = result.Error switch
        {
            SwapResult.InvalidDetails => StatusCodes.Status400BadRequest,
            SponsorshipChangeResult.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }

    /// <summary>A day's outcome as the API names it: camelCase, like its other codes' readers expect.</summary>
    private static string OutcomeName(StandingDayOutcome outcome) => outcome switch
    {
        StandingDayOutcome.Booked => "booked",
        StandingDayOutcome.NotBooked => "notBooked",
        StandingDayOutcome.AlreadyDone => "alreadyDone",
        StandingDayOutcome.AlreadyHeld => "alreadyHeld",
        StandingDayOutcome.Ended => "ended",
        StandingDayOutcome.Busy => "busy",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");

    private static IResult Refused(SponsorshipChangeResult result)
    {
        var status = result.Error switch
        {
            SponsorshipChangeResult.InvalidDetails => StatusCodes.Status400BadRequest,
            SponsorshipChangeResult.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }
}

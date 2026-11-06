using Microsoft.AspNetCore.Mvc;
using Sethika.Api.RateLimiting;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Giving.Payments;
using Sethika.Domain.Giving;

namespace Sethika.Api.Endpoints;

/// <summary>
/// Giving online through PayHere (plan 09, D.3): an institution's merchant settings, a donor's order and
/// return page, and PayHere's notification.
/// </summary>
/// <remarks>
/// The notification is anonymous because PayHere has no account here: its signature is the credential.
/// Every refusal looks the same from outside, so it cannot be used to find out which orders exist. It is
/// read from the form by hand, because <c>[FromForm]</c> would ask for an antiforgery token PayHere cannot
/// send. The return URL proves nothing: the return page only reads what the notification left (M9).
/// </remarks>
internal static class PaymentEndpoints
{
    private const string PaymentsView = "GIVING.PAYMENTS.VIEW";
    private const string PaymentsEdit = "GIVING.PAYMENTS.EDIT";
    private const string MyGivingView = "MYGIVING.DONATIONS.VIEW";
    private const string MyGivingCreate = "MYGIVING.DONATIONS.CREATE";

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var merchant = app.MapGroup("/api/institution/payments/merchant");

        merchant.MapGet("", MerchantAsync).WithMetadata(new HasPermissionAttribute(PaymentsView));
        merchant.MapPut("", SaveMerchantAsync).WithMetadata(new HasPermissionAttribute(PaymentsEdit));

        app.MapPost("/api/institutions/{slug}/give", GiveAsync)
            .WithMetadata(new HasPermissionAttribute(MyGivingCreate))
            .RateLimitPerUser(PaymentRateLimits.GivePerUser);

        app.MapGet("/api/me/payments/{orderId:guid}", MyPaymentAsync)
            .WithMetadata(new HasPermissionAttribute(MyGivingView));

        app.MapPost("/api/payments/payhere/notify", NotifyAsync)
            .AllowAnonymous()
            .RateLimitPerClient(PaymentRateLimits.NotifyPerClient);

        return app;
    }

    private static async Task<IResult> MerchantAsync(ICurrentUserContext currentUser, IPaymentQueries queries, CancellationToken cancellationToken)
    {
        if (currentUser.InstitutionId is not { } institutionId)
        {
            return NoInstitution();
        }

        return Results.Ok(new { merchant = await queries.GetMerchantAsync(institutionId, cancellationToken) });
    }

    private static async Task<IResult> SaveMerchantAsync([FromBody] MerchantInput body, ICurrentUserContext currentUser, PaymentMerchantHandler handler, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } userId, InstitutionId: { } institutionId })
        {
            return NoInstitution();
        }

        var result = await handler.SaveAsync(institutionId, userId, body, cancellationToken);
        if (result.Succeeded)
        {
            return Results.Ok(new { version = result.Version });
        }

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: StatusCodes.Status400BadRequest)
            : ApiErrors.Error(StatusCodes.Status409Conflict, result.Error!);
    }

    private static async Task<IResult> GiveAsync(string slug, [FromBody] GiveInput body, ICurrentUserContext currentUser, IPlatformPayments payments, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await payments.StartAsync(userId, slug, body, cancellationToken);
        if (result.Succeeded)
        {
            return Results.Json(result.Form, statusCode: StatusCodes.Status201Created);
        }

        var status = result.Error switch
        {
            GiveResult.InvalidDetails => StatusCodes.Status400BadRequest,
            GiveResult.InstitutionNotFound or NeedErrors.NotFound => StatusCodes.Status404NotFound,
            GiveResult.PhoneRequired => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict,
        };

        return result.Fields.Count > 0
            ? Results.Json(new { code = result.Error, fields = result.Fields }, statusCode: status)
            : ApiErrors.Error(status, result.Error!);
    }

    private static async Task<IResult> MyPaymentAsync(Guid orderId, ICurrentUserContext currentUser, IPaymentQueries queries, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return await queries.GetMyPaymentAsync(userId, orderId, cancellationToken) is { } payment
            ? Results.Ok(payment)
            : ApiErrors.Error(StatusCodes.Status404NotFound, "payment_not_found");
    }

    private static async Task<IResult> NotifyAsync(HttpRequest request, IPlatformPayments payments, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Refused();
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var notification = new PayHereNotification(
            Field("merchant_id"),
            Field("order_id"),
            Field("payment_id"),
            Field("payhere_amount"),
            Field("payhere_currency"),
            Field("status_code"),
            Field("md5sig"));

        return await payments.NotifyAsync(notification, cancellationToken) ? Results.Ok() : Refused();

        string? Field(string name) => form.TryGetValue(name, out var value) ? value.ToString() : null;
    }

    private static IResult Refused() => ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_notification");

    /// <summary>Unreachable through <c>[HasPermission]</c>, but the handlers are not run on that assumption.</summary>
    private static IResult NoInstitution() => ApiErrors.Error(StatusCodes.Status403Forbidden, "no_institution");
}

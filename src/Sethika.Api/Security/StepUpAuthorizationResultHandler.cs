using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Sethika.Api.Endpoints;

namespace Sethika.Api.Security;

/// <summary>
/// Answers a refusal that only a step-up would lift with <c>403 { "code": "stepup_required" }</c>,
/// so the client can ask for a code and retry instead of showing "not allowed".
/// </summary>
/// <remarks>
/// The permission handler marks the request rather than calling <c>Fail()</c> with a reason. A
/// handler that does not succeed is already a refusal; the mark only chooses the body. Every other
/// outcome goes to the framework's own handler unchanged.
/// </remarks>
public sealed class StepUpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private const string StepUpRequiredItem = "sethika:stepup-required";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <summary>Marks the request as refused for want of a step-up. The resource is its HttpContext.</summary>
    public static void MarkStepUpRequired(object? resource)
    {
        if (resource is HttpContext httpContext)
        {
            httpContext.Items[StepUpRequiredItem] = true;
        }
    }

    /// <summary>True when the permission handler asked for a step-up on this request.</summary>
    public static bool IsStepUpRequired(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.ContainsKey(StepUpRequiredItem);
    }

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        return authorizeResult.Forbidden && IsStepUpRequired(context)
            ? ApiErrors.Error(StatusCodes.Status403Forbidden, "stepup_required").ExecuteAsync(context)
            : _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

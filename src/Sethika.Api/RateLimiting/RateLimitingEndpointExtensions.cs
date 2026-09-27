using Sethika.Api.Endpoints;
using Sethika.Application.Abstractions;

namespace Sethika.Api.RateLimiting;

/// <summary>Applies an <see cref="IRateLimiter"/> rule to an endpoint, as an endpoint filter.</summary>
/// <remarks>
/// A filter rather than middleware because the subject often lives in the request body - the login
/// name, the phone number - and a filter sees the bound arguments. Filters run in the order they are
/// added, so the cheap per-client rule goes first and a request it refuses never touches the
/// per-identity counter.
/// </remarks>
internal static class RateLimitingEndpointExtensions
{
    /// <summary>Limits by the client's address.</summary>
    public static RouteHandlerBuilder RateLimitPerClient(this RouteHandlerBuilder builder, RateLimitRule rule) =>
        builder.RateLimit(rule, context => ClientAddress.Of(context.HttpContext));

    /// <summary>Limits by a value taken from the request body.</summary>
    /// <param name="builder">The endpoint.</param>
    /// <param name="rule">The limit.</param>
    /// <param name="subject">
    /// Picks the subject from the body. Null skips the rule: a body without the field fails its own
    /// validation, and the per-client rule has already counted it.
    /// </param>
    public static RouteHandlerBuilder RateLimitBy<TBody>(
        this RouteHandlerBuilder builder,
        RateLimitRule rule,
        Func<TBody, IServiceProvider, string?> subject) =>
        builder.RateLimit(rule, context =>
            context.Arguments.OfType<TBody>().FirstOrDefault() is { } body
                ? subject(body, context.HttpContext.RequestServices)
                : null);

    private static RouteHandlerBuilder RateLimit(
        this RouteHandlerBuilder builder,
        RateLimitRule rule,
        Func<EndpointFilterInvocationContext, string?> subjectOf) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var subject = subjectOf(context);
            if (subject is null)
            {
                return await next(context).ConfigureAwait(false);
            }

            var limiter = context.HttpContext.RequestServices.GetRequiredService<IRateLimiter>();
            var decision = await limiter
                .AcquireAsync(rule, subject, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (decision.IsAllowed)
            {
                return await next(context).ConfigureAwait(false);
            }

            return ApiErrors.TooManyRequests(context.HttpContext, decision.RetryAfter);
        });
}

/// <summary>The client's address as the rate limits see it.</summary>
internal static class ClientAddress
{
    /// <summary>
    /// <see cref="ConnectionInfo.RemoteIpAddress"/>, which <c>UseForwardedHeaders</c> has already
    /// replaced with the forwarded client address when - and only when - the request came through a
    /// trusted proxy.
    /// </summary>
    public static string Of(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

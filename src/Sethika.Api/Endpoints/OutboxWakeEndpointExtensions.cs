using Sethika.Application.Abstractions;

namespace Sethika.Api.Endpoints;

/// <summary>Wakes the outbox after an endpoint whose change wrote outbox messages (plan 07, H10).</summary>
internal static class OutboxWakeEndpointExtensions
{
    /// <summary>
    /// After a success (2xx) only: the change has committed by then, and a refusal wrote nothing. The
    /// open calendars and the texts hear within about a second instead of at the next minute's run.
    /// </summary>
    public static RouteHandlerBuilder WakesOutbox(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
            {
                context.HttpContext.RequestServices.GetRequiredService<IOutboxWake>().Wake();
            }

            return result;
        });
}

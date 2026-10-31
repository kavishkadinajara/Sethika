namespace Sethika.Application.Abstractions;

/// <summary>A limit: at most <paramref name="Limit"/> requests per <paramref name="Window"/>.</summary>
/// <param name="Name">Part of the counter's key, so two rules never share a count.</param>
/// <param name="Limit">Requests allowed per window, per subject.</param>
/// <param name="Window">Length of the fixed window.</param>
public sealed record RateLimitRule(string Name, int Limit, TimeSpan Window);

/// <summary>Whether a request may proceed.</summary>
/// <param name="IsAllowed">True when the request is within the limit.</param>
/// <param name="RetryAfter">When refused, how long until the window resets.</param>
public readonly record struct RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    /// <summary>The request may proceed.</summary>
    public static RateLimitDecision Allowed { get; } = new(true, TimeSpan.Zero);

    /// <summary>The request is over the limit until <paramref name="retryAfter"/> has passed.</summary>
    public static RateLimitDecision Denied(TimeSpan retryAfter) => new(false, retryAfter);
}

/// <summary>
/// Counts requests per subject - a login name, a phone number, a client address - across every API
/// instance.
/// </summary>
/// <remarks>
/// Shared, not per process: the in-box ASP.NET Core limiter counts inside one instance, so behind a
/// load balancer an attacker's budget multiplies by the instance count (plan A12).
///
/// When the store is unreachable the request is allowed and a warning is logged. A cache outage
/// must not lock every user out - the same choice the permission cache makes, and the opposite of
/// one-time codes, which have no safe fallback. The account lockout still applies to sign-in.
/// </remarks>
public interface IRateLimiter
{
    /// <summary>Counts one request by <paramref name="subject"/> against <paramref name="rule"/>.</summary>
    /// <param name="rule">The limit to apply.</param>
    /// <param name="subject">Who or what is being limited. Never stored as given.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<RateLimitDecision> AcquireAsync(
        RateLimitRule rule,
        string subject,
        CancellationToken cancellationToken = default);
}

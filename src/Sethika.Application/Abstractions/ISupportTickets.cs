namespace Sethika.Application.Abstractions;

/// <summary>
/// One-use permission to enter an institution as platform support (plan 04, Y1a). Issued behind a
/// step-up by a Bearer endpoint; spent by the cookie-authenticated switch, which has no access token
/// to check a step-up against.
/// </summary>
public interface ISupportTickets
{
    /// <summary>How long a ticket waits to be spent.</summary>
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Issues a ticket for <paramref name="userId"/> to enter <paramref name="institutionId"/>. False if it could not be stored.</summary>
    Task<bool> IssueAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default);

    /// <summary>Spends the ticket: true once, then false. False too when the store cannot answer.</summary>
    Task<bool> SpendAsync(Guid userId, int institutionId, CancellationToken cancellationToken = default);
}

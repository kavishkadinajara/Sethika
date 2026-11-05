using Sethika.Application.Giving.Needs;

namespace Sethika.Application.Abstractions;

/// <summary>
/// A donor's own pledge and its cancel (plan 09, M13, M14).
/// </summary>
/// <remarks>
/// Both move a counter on a need that belongs to another tenant, which a donor's session may not write
/// (the block after update on <c>giving.Needs</c>), so these two run with the platform's reach. Their
/// only inputs are the caller's id and an address, and <see cref="PledgeHandler"/> acts only for the
/// caller: nothing else is reachable.
/// </remarks>
public interface IOwnPledges
{
    /// <summary>The caller pledges part of the need <paramref name="needId"/>.</summary>
    Task<NeedResult> PledgeAsync(Guid userId, Guid needId, PledgeInput input, CancellationToken cancellationToken = default);

    /// <summary>The caller cancels their own open pledge <paramref name="pledgeId"/>.</summary>
    Task<NeedResult> CancelAsync(Guid userId, Guid pledgeId, CancellationToken cancellationToken = default);
}

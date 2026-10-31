namespace Sethika.Application.Abstractions;

/// <summary>
/// Records that the holder of one access token re-verified recently, and answers whether they did
/// (plan A5).
/// </summary>
/// <remarks>
/// A grant belongs to a user and one access token's <c>jti</c>, and expires on its own. It is not a
/// claim in the token: that would mean re-issuing the token mid-flow. Because it is bound to the
/// <c>jti</c>, a refreshed token starts without one - whoever refreshes a stolen refresh token does
/// not inherit the victim's step-up.
///
/// Both methods fail closed. A store that cannot be reached grants nothing and confirms nothing, so
/// sensitive actions stop during an outage rather than going through unverified.
/// </remarks>
public interface IStepUpGrants
{
    /// <summary>Grants step-up to this user's token for <paramref name="lifetime"/>.</summary>
    /// <returns>False when the grant could not be stored.</returns>
    Task<bool> GrantAsync(Guid userId, string tokenId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>True only when a live grant exists for this user and this token.</summary>
    Task<bool> IsGrantedAsync(Guid userId, string tokenId, CancellationToken cancellationToken = default);
}

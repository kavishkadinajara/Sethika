using Sethika.Application.Giving.Payments;

namespace Sethika.Application.Abstractions;

/// <summary>
/// The two payment steps that reach beyond a session (plan 09, M8-M10).
/// </summary>
/// <remarks>
/// A donor's session may not read a merchant (<c>sec.fn_Tenant</c>), and PayHere's notification has no
/// session, so both run with the platform's reach, as <see cref="IOwnPledges"/> does (M14). The first takes
/// only the caller's id and the address they named; the second is believed only once its signature is
/// checked with the institution's own secret.
/// </remarks>
public interface IPlatformPayments
{
    /// <summary>The caller starts a gift to the institution <paramref name="slug"/>.</summary>
    Task<GiveResult> StartAsync(Guid userId, string slug, GiveInput input, CancellationToken cancellationToken = default);

    /// <summary>Applies PayHere's notification. <see langword="false"/> when it is refused.</summary>
    Task<bool> NotifyAsync(PayHereNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether an institution takes gifts online (M10), for its public page, which has no session that could
/// read a merchant. Answers one yes or no and nothing about the merchant.
/// </summary>
public interface IOnlineGiving
{
    Task<bool> IsOnAsync(string slug, CancellationToken cancellationToken = default);
}

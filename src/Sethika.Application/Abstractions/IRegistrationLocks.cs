namespace Sethika.Application.Abstractions;

/// <summary>
/// Serialises decisions on applications, and one applicant's new applications, inside the caller's
/// transaction (plan 02, B3 and B6).
/// </summary>
/// <remarks>
/// A decision takes two locks, both held until the transaction ends:
/// <list type="number">
/// <item>One application lock that every approval and rejection takes, so decisions happen one at a
/// time. They are rare - a platform administrator pressing a button - and this makes everything an
/// approval chooses race-free: the institution code, the slug, the owner. A single named lock has
/// no range to deadlock on.</item>
/// <item>An update lock on the application's row, so a reviewer's change waits for the decision
/// instead of being lost under it, and a second decision on the same application, once it gets in,
/// reads the application as the first left it.</item>
/// </list>
/// </remarks>
public interface IRegistrationLocks
{
    /// <summary>Takes both locks.</summary>
    /// <returns>Whether the application exists and the caller may see it.</returns>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task<bool> ClaimForDecisionAsync(string requestNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes an update lock on the applicant's own user row, so one person's new applications are
    /// counted and created one at a time: two at once cannot both see room under the limit.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the context.</exception>
    Task LockApplicantAsync(Guid userId, CancellationToken cancellationToken = default);
}

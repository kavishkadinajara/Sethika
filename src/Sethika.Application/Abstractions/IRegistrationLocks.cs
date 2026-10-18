namespace Sethika.Application.Abstractions;

/// <summary>
/// Serialises decisions on applications, inside the caller's transaction (plan 02, B6).
/// </summary>
/// <remarks>
/// Two locks, both held until the transaction ends:
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
}

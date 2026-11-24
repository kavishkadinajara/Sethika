namespace Sethika.Application.Abstractions;

/// <summary>Every grant a role ever held, from <c>auth.RolePermissions</c>' temporal history (plan 09, Q4).</summary>
public interface IRoleGrantHistory
{
    /// <summary>
    /// The (role, permission) pairs the roles hold now or held at any time, read on the context's own
    /// connection and transaction.
    /// </summary>
    Task<IReadOnlyCollection<(int RoleId, int PermissionId)>> EverHeldAsync(IReadOnlyList<int> roleIds, CancellationToken cancellationToken = default);
}

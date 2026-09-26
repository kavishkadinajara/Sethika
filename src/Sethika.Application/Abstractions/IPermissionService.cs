using Sethika.Application.Access;

namespace Sethika.Application.Abstractions;

public interface IPermissionService
{
    Task<PermissionSet> GetEffectivePermissionsAsync(
        Guid userId,
        int? institutionId,
        CancellationToken cancellationToken = default);
}

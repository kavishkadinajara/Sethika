using System.Data.Common;

namespace Sethika.Application.Abstractions;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

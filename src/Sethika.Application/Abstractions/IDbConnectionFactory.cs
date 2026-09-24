using System.Data.Common;

namespace Sethika.Application.Abstractions;

/// <summary>
/// Opens connections for the Dapper read path. The returned connection is already
/// open and already stamped with the session context, so callers never have to
/// remember to do it - forgetting would silently bypass tenant isolation.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

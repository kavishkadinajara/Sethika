using System.Data.Common;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Infrastructure.Queries;

/// <summary>
/// Opens connections for the Dapper read path, already stamped with the tenant
/// session context so a caller cannot forget to do it.
/// </summary>
public sealed class SqlConnectionFactory(string connectionString, ICurrentUserContext currentUser)
    : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SessionContext.ApplyAsync(connection, currentUser, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            // never hand back - or leak - a connection that is not stamped
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

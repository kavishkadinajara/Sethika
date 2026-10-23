using System.Data.Common;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;

namespace Sethika.Api.IntegrationTests.Notifications;

/// <summary>Opens a connection stamped as the platform, so notify.Notifications is readable.</summary>
internal sealed class PlatformConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXEC sys.sp_set_session_context @key = N'UserId',        @value = NULL, @read_only = 1;
            EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = NULL, @read_only = 1;
            EXEC sys.sp_set_session_context @key = N'IsPlatform',    @value = 1,    @read_only = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}

using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Persistence;

internal static class SessionContext
{
    private const string StampSql =
        """
        EXEC sys.sp_set_session_context @key = N'UserId',        @value = @userId,        @read_only = 1;
        EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = @institutionId, @read_only = 1;
        EXEC sys.sp_set_session_context @key = N'IsPlatform',    @value = @isPlatform,    @read_only = 1;
        """;

    internal static async Task ApplyAsync(
        DbConnection connection,
        ICurrentUserContext currentUser,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection, currentUser);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void Apply(DbConnection connection, ICurrentUserContext currentUser)
    {
        using var command = CreateCommand(connection, currentUser);
        command.ExecuteNonQuery();
    }

    private static DbCommand CreateCommand(DbConnection connection, ICurrentUserContext currentUser)
    {
        var command = connection.CreateCommand();
        command.CommandText = StampSql;

        Add(command, "@userId", SqlDbType.UniqueIdentifier, currentUser.UserId);
        Add(command, "@institutionId", SqlDbType.Int, currentUser.InstitutionId);
        Add(command, "@isPlatform", SqlDbType.Bit, currentUser.IsPlatform);

        return command;
    }

    private static void Add(DbCommand command, string name, SqlDbType type, object? value)
    {
        var parameter = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
        command.Parameters.Add(parameter);
    }
}

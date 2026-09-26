using System.Globalization;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Persistence;

public sealed class SethikaTestDatabase : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Server=localhost;Database=Sethika_Test;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=True";

    /// <summary>Override with SETHIKA_TEST_SQL to point at another server.</summary>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("SETHIKA_TEST_SQL") ?? DefaultConnectionString;

    /// <summary>
    /// Override with SETHIKA_TEST_REDIS to point at another Redis.
    /// </summary>
    /// <remarks>
    /// defaultDatabase=3 keeps Sethika off db 0, which on this machine is shared with another
    /// application's cache. Without it a FLUSHDB while testing would wipe that app's keys,
    /// and both would compete for the same eviction budget.
    /// </remarks>
    public string RedisConnectionString { get; } =
        Environment.GetEnvironmentVariable("SETHIKA_TEST_REDIS") ?? "127.0.0.1:6379,defaultDatabase=3";

    /// <summary>Fixed in the seed script so tests can reference them without a lookup.</summary>
    public static Guid AlphaUserId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Guid BetaUserId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public int AlphaInstitutionId { get; private set; }
    public int BetaInstitutionId { get; private set; }

    public async Task InitializeAsync()
    {
        await EnsureSchemaPresentAsync().ConfigureAwait(false);
        await SeedAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task EnsureSchemaPresentAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.security_policies WHERE name = 'TenantIsolationPolicy'";

            var policies = Convert.ToInt32(
                await command.ExecuteScalarAsync().ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (policies == 0)
            {
                throw new InvalidOperationException("the database exists but has no TenantIsolationPolicy");
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"""
                 Could not reach a prepared Sethika_Test database ({ex.Message}).

                 These tests need the real schema, because Row-Level Security cannot be
                 faked in memory. Create it with:

                   dotnet build src/Sethika.Database
                   SqlPackage /Action:Publish ^
                     /SourceFile:src/Sethika.Database/bin/Debug/Sethika.Database.dacpac ^
                     /TargetServerName:localhost /TargetDatabaseName:Sethika_Test ^
                     /TargetTrustServerCertificate:True

                 Point elsewhere by setting SETHIKA_TEST_SQL.
                 """, ex);
        }
    }

    /// <summary>
    /// Reseeds through a platform connection: the RLS BLOCK predicates would otherwise
    /// refuse inserts that span two tenants.
    /// </summary>
    /// <remarks>
    /// Public so a test that mutates shared state - a deny override, a disabled module - can
    /// put the database back. Tests within a class run sequentially but in no guaranteed
    /// order, so each one reseeding is what keeps them independent.
    /// </remarks>
    public async Task SeedAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (var stamp = connection.CreateCommand())
        {
            stamp.CommandText =
                """
                EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;
                SELECT CAST(SESSION_CONTEXT(N'IsPlatform') AS bit);
                """;

            var applied = await stamp.ExecuteScalarAsync().ConfigureAwait(false);
            if (applied is not true)
            {
                throw new InvalidOperationException(
                    $"IsPlatform did not take on the seeding connection (got '{applied ?? "null"}'). " +
                    "Without it the RLS predicates hide the existing rows, so the reset silently " +
                    "deletes nothing and every insert then collides.");
            }
        }

        // The reset has to be atomic and serialized, or two seeders interleave, one is picked
        // as the deadlock victim, and its DELETEs roll back - leaving rows that make the next
        // run fail on duplicate keys, a long way from the cause.
        //
        // sp_getapplock serializes across processes; the transaction means a failed run
        // leaves the data exactly as it was.
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync().ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            SET XACT_ABORT ON;
            DECLARE @lock int;
            EXEC @lock = sys.sp_getapplock @Resource = N'Sethika.RlsTestData',
                                           @LockMode = N'Exclusive',
                                           @LockOwner = N'Transaction',
                                           @LockTimeout = 30000;
            IF @lock < 0 THROW 50000, 'Timed out waiting for the test-seed lock.', 1;
            """).ConfigureAwait(false);

        // Each GO-separated batch runs as its own command. The script toggles SYSTEM_VERSIONING
        // off and on, and SQL Server validates a whole batch before executing any of it - so a
        // DELETE against a history table has to be compiled after, not alongside, the ALTER
        // that stopped it being one.
        foreach (var batch in SplitBatches(ReadSeedScript()))
        {
            await ExecuteAsync(connection, transaction, batch).ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        await using var read = connection.CreateCommand();
        read.CommandText =
            "SELECT InstitutionCode, Id FROM org.Institutions WHERE InstitutionCode IN ('ALPHA','BETA')";

        await using var reader = await read.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader.GetString(0) == "ALPHA")
            {
                AlphaInstitutionId = reader.GetInt32(1);
            }
            else
            {
                BetaInstitutionId = reader.GetInt32(1);
            }
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 60;

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Splits the script on lines that are just GO.
    /// </summary>
    /// <remarks>
    /// GO is a client-side batch separator, not T-SQL: SqlCommand rejects it. Splitting here
    /// is what lets one file contain DDL and statements that depend on that DDL.
    /// </remarks>
    private static IEnumerable<string> SplitBatches(string script)
    {
        var batches = script
            .Split(["\nGO\n", "\r\nGO\r\n"], StringSplitOptions.None)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0);

        return [.. batches];
    }

    private static string ReadSeedScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith("RlsTestData.sql", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("RlsTestData.sql is not embedded in the test assembly.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

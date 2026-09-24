using System.Globalization;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Points the persistence tests at a dedicated Sethika_Test database and reseeds it once
/// per test class run.
/// </summary>
/// <remarks>
/// A real SQL Server is required: Row-Level Security, session context and the sec
/// predicate functions have no in-memory equivalent, so an in-memory provider would
/// prove nothing about tenant isolation.
/// </remarks>
public sealed class SethikaTestDatabase : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Server=localhost;Database=Sethika_Test;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=True";

    /// <summary>Override with SETHIKA_TEST_SQL to point at another server.</summary>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("SETHIKA_TEST_SQL") ?? DefaultConnectionString;

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
    private async Task SeedAsync()
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

        await using (var seed = connection.CreateCommand())
        {
            // The reset has to be atomic and serialized, or two seeders interleave, one
            // is picked as the deadlock victim, and its DELETEs roll back - leaving rows
            // that make the next run fail on duplicate keys, a long way from the cause.
            //
            // sp_getapplock serializes across processes; the transaction means a failed
            // run leaves the data exactly as it was. XACT_ABORT makes the first real
            // error surface as itself rather than as six downstream duplicates.
            seed.CommandText =
                "SET XACT_ABORT ON;" + Environment.NewLine +
                "BEGIN TRANSACTION;" + Environment.NewLine +
                """
                DECLARE @lock int;
                EXEC @lock = sys.sp_getapplock @Resource = N'Sethika.RlsTestData',
                                               @LockMode = N'Exclusive',
                                               @LockOwner = N'Transaction',
                                               @LockTimeout = 30000;
                IF @lock < 0 THROW 50000, 'Timed out waiting for the test-seed lock.', 1;
                """ + Environment.NewLine +
                ReadSeedScript() + Environment.NewLine +
                "COMMIT TRANSACTION;";

            seed.CommandTimeout = 60;
            await seed.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

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

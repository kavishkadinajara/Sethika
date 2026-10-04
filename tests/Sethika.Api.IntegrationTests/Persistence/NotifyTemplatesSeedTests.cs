using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// <c>11_NotifyTemplates.sql</c>, the shipped seed itself, against the published database.
/// </summary>
/// <remarks>
/// As in <see cref="RbacCatalogueTests"/>, everything that changes reference data runs in a
/// transaction that is rolled back.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class NotifyTemplatesSeedTests(SethikaTestDatabase database)
{
    // Body compared as binary, so a case-only or zero-width-joiner difference counts.
    private const string Snapshot =
        """
        SELECT CONVERT(VARCHAR(6), t.Id) + ' ' + t.Code + ' ' + t.Channel + ' ' + t.Language + ' '
               + CONVERT(VARCHAR(1), t.IsActive) + ' ' + CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', t.Body), 2)
        FROM notify.Templates AS t
        ORDER BY t.Id;
        """;

    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> BeginAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return (connection, (SqlTransaction)await connection.BeginTransactionAsync());
    }

    private static string TemplatesSeed()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("11_NotifyTemplates.sql", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("11_NotifyTemplates.sql is not embedded in the test assembly.");

        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private static async Task<List<string>> SnapshotAsync(SqlConnection connection, SqlTransaction transaction) =>
        [.. await connection.QueryAsync<string>(Snapshot, transaction: transaction)];

    [Fact]
    public async Task RunningTheSeedTwiceChangesNothing()
    {
        // It runs on every publish. Ids are compared too: the rows are updated in place, never
        // replaced, because notify.Notifications.TemplateId points at them.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var before = await SnapshotAsync(connection, transaction);

            await connection.ExecuteAsync(TemplatesSeed(), transaction: transaction);
            await connection.ExecuteAsync(TemplatesSeed(), transaction: transaction);

            Assert.Equal(before, await SnapshotAsync(connection, transaction));
        }
    }

    [Fact]
    public async Task EveryCodeSendingPurposeHasOneActiveSmsTemplateInEachLanguage_WithTheCodeInIt()
    {
        await using var connection = new SqlConnection(database.ConnectionString);

        var rows = (await connection.QueryAsync<(string Code, string Language, string Body)>(
            """
            SELECT t.Code AS Code, t.Language AS Language, t.Body AS Body
            FROM notify.Templates AS t
            WHERE t.Channel = 'SMS' AND t.IsActive = 1 AND t.Code LIKE 'OTP[_]%'
            ORDER BY t.Code, t.Language;
            """)).ToList();

        Assert.Equal(
            ["OTP_SIGNUP en", "OTP_SIGNUP si", "OTP_SIGNUP ta", "OTP_STEPUP en", "OTP_STEPUP si", "OTP_STEPUP ta"],
            rows.Select(r => $"{r.Code} {r.Language}"));

        Assert.All(rows, r => Assert.Contains("{code}", r.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABodyEditedInTheDatabaseIsPutBack_ButASwitchedOffTemplateStaysOff()
    {
        // The seed owns the wording. Whether a template is in use is an operator's call.
        var (connection, transaction) = await BeginAsync();
        await using (connection)
        await using (transaction)
        {
            var before = await SnapshotAsync(connection, transaction);

            await connection.ExecuteAsync(
                """
                UPDATE notify.Templates SET Body = UPPER(Body) WHERE Code = 'OTP_SIGNUP' AND Language = 'en';
                UPDATE notify.Templates SET IsActive = 0     WHERE Code = 'OTP_STEPUP' AND Language = 'ta';
                """,
                transaction: transaction);

            await connection.ExecuteAsync(TemplatesSeed(), transaction: transaction);

            var expected = before.Select(row => row.Contains(" OTP_STEPUP SMS ta 1 ", StringComparison.Ordinal)
                ? row.Replace(" OTP_STEPUP SMS ta 1 ", " OTP_STEPUP SMS ta 0 ", StringComparison.Ordinal)
                : row);

            Assert.Equal(expected, await SnapshotAsync(connection, transaction));
        }
    }
}

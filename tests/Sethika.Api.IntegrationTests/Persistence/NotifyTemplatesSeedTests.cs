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
            [
                "OTP_LOGIN en", "OTP_LOGIN si", "OTP_LOGIN ta",
                "OTP_RESET en", "OTP_RESET si", "OTP_RESET ta",
                "OTP_SIGNUP en", "OTP_SIGNUP si", "OTP_SIGNUP ta",
                "OTP_STEPUP en", "OTP_STEPUP si", "OTP_STEPUP ta",
            ],
            rows.Select(r => $"{r.Code} {r.Language}"));

        Assert.All(rows, r => Assert.Contains("{code}", r.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryOnboardingTextHasOneActiveSmsTemplateInEachLanguage_WithTheRequestNumberInIt()
    {
        await using var connection = new SqlConnection(database.ConnectionString);

        var rows = (await connection.QueryAsync<(string Code, string Language, string Body)>(
            """
            SELECT t.Code AS Code, t.Language AS Language, t.Body AS Body
            FROM notify.Templates AS t
            WHERE t.Channel = 'SMS' AND t.IsActive = 1 AND t.Code LIKE 'ONBOARD[_]%'
            ORDER BY t.Code, t.Language;
            """)).ToList();

        Assert.Equal(
            [
                "ONBOARD_APPROVED en", "ONBOARD_APPROVED si", "ONBOARD_APPROVED ta",
                "ONBOARD_INFO_REQUESTED en", "ONBOARD_INFO_REQUESTED si", "ONBOARD_INFO_REQUESTED ta",
                "ONBOARD_REJECTED en", "ONBOARD_REJECTED si", "ONBOARD_REJECTED ta",
                "ONBOARD_SUBMITTED en", "ONBOARD_SUBMITTED si", "ONBOARD_SUBMITTED ta",
            ],
            rows.Select(r => $"{r.Code} {r.Language}"));

        // The placeholder is the only one, and the zero-width joiner in ප්‍රශ්නයක් survived the build.
        Assert.All(rows, r => Assert.Contains("{requestNo}", r.Body, StringComparison.Ordinal));
        Assert.Contains('‍', rows.Single(r => r is { Code: "ONBOARD_INFO_REQUESTED", Language: "si" }).Body);
    }

    [Fact]
    public async Task EveryBookingTextHasOneActiveSmsTemplateInEachLanguage_OpeningWithTheSameLine()
    {
        // Plan 03, C11. The opening line names the booking, so a donor with several can tell which
        // one a text is about; it is identical in every language, so no placeholder is inflected.
        await using var connection = new SqlConnection(database.ConnectionString);

        var rows = (await connection.QueryAsync<(string Code, string Language, string Body)>(
            """
            SELECT t.Code AS Code, t.Language AS Language, t.Body AS Body
            FROM notify.Templates AS t
            WHERE t.Channel = 'SMS' AND t.IsActive = 1 AND t.Code LIKE 'BOOKING[_]%'
            ORDER BY t.Code, t.Language;
            """)).ToList();

        string[] codes = ["APPROVED", "CANCELLED", "CONFIRMED", "REJECTED", "REMINDER", "REQUESTED"];
        string[] languages = ["en", "si", "ta"];
        Assert.Equal(
            codes.SelectMany(c => languages.Select(l => $"BOOKING_{c} {l}")),
            rows.Select(r => $"{r.Code} {r.Language}"));

        Assert.All(rows, r => Assert.StartsWith("Sethika {bookingNo}: {institution}, {date} {time}. ", r.Body, StringComparison.Ordinal));
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

using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// The role-level schema (plan A15) and the reference data signup depends on, read back from the
/// published database rather than from the seed scripts.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RoleLevelSchemaTests(SethikaTestDatabase database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task ARoleCannotSitAtALevelOfAnotherScope()
    {
        // An institution role at PLATFORM_ADMIN would carry a platform rank into a tenant. The
        // composite FK (RoleLevelId, Scope) is what refuses it; a single-column FK would not.
        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();

        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'PLATFORM_ADMIN'),
                    'CROSS_SCOPE', N'Cross scope', 0);
            """,
            new { institutionId = database.AlphaInstitutionId },
            transaction));

        Assert.Contains("FK_Roles_Level", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARoleAtALevelOfItsOwnScopeIsAccepted()
    {
        // The control for the test above: the same insert at an Institution level succeeds, so the
        // refusal there is about scope and not some other constraint.
        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();

        var inserted = await connection.ExecuteAsync(
            """
            INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
            VALUES (@institutionId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'MANAGER'),
                    'SAME_SCOPE', N'Same scope', 0);
            """,
            new { institutionId = database.AlphaInstitutionId },
            transaction);

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task TheDonorRoleIsAGlobalSystemRoleAtTheDonorLevel_AndSurvivesTheFixtureReset()
    {
        // InitializeAsync has just reset the fixture, which deletes every non-system role. Signup
        // assigns this role, so if the reset took it too, every signup test would fail for a reason
        // a long way from its cause.
        await using var connection = await OpenAsync();

        var donor = await connection.QuerySingleAsync<(string Scope, int? InstitutionId, bool IsSystem, bool IsTemplate, string Level)>(
            """
            SELECT r.Scope, r.InstitutionId, r.IsSystem, r.IsTemplate, l.Code
            FROM auth.Roles r JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
            WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """);

        Assert.Equal("Donor", donor.Scope);
        Assert.Null(donor.InstitutionId);
        Assert.True(donor.IsSystem);
        Assert.False(donor.IsTemplate);
        Assert.Equal("DONOR", donor.Level);
    }

    [Fact]
    public async Task EachScopeHasOneMostSeniorLevel()
    {
        await using var connection = await OpenAsync();

        var mostSenior = (await connection.QueryAsync<string>(
            "SELECT Code FROM auth.RoleLevels WHERE Rank = 1 ORDER BY Scope;")).ToList();

        Assert.Equal(["DONOR", "OWNER", "PLATFORM_ADMIN"], mostSenior);
    }

    [Fact]
    public async Task EveryLegalDocumentHashIsTheSha256OfItsUtf16Content()
    {
        // The hash is the proof of what a user accepted, so whatever re-verifies it has to know the
        // encoding. The seed documents UTF-16LE (NVARCHAR as stored), lowercase hex; this holds the
        // stored rows to it, including the Sinhala and Tamil ones that would expose a build that
        // mangled the text on the way in.
        await using var connection = await OpenAsync();

        var documents = (await connection.QueryAsync<(string Code, string Language, string Content, string ContentSha256)>(
            "SELECT Code, Language, Content, ContentSha256 FROM core.LegalDocuments;")).ToList();

        Assert.Equal(6, documents.Count);

        foreach (var document in documents)
        {
            var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(document.Content)));

            Assert.True(
                expected == document.ContentSha256,
                $"{document.Code}/{document.Language}: stored {document.ContentSha256}, content hashes to {expected}");
        }
    }

    [Fact]
    public async Task EveryCurrentLegalDocumentIsMarkedDraft()
    {
        // Q4: until real terms exist, nothing may look like agreed terms. Remove this test in the
        // same commit that seeds the first real version - not before.
        await using var connection = await OpenAsync();

        var titles = await connection.QueryAsync<string>(
            "SELECT Title FROM core.LegalDocuments WHERE IsCurrent = 1;");

        Assert.All(titles, title => Assert.Contains("0.1-draft", title, StringComparison.Ordinal));
    }
}

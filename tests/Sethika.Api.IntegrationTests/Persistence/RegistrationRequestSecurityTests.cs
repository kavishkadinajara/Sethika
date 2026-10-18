using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Plan 02, B2: an application is seen by its applicant, by the platform, and once approved by the
/// institution it created - and by nobody else. Enforced by Row-Level Security, so every query here
/// is a plain "all applications" with no WHERE on the applicant.
/// </summary>
/// <remarks>
/// The fixture holds REQ-ALPHA (Alpha's) and REQ-BETA (Beta's), both Draft and neither linked to an
/// institution yet. A test that links or changes one commits it, so the fixture is reset after
/// every test as well as before.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RegistrationRequestSecurityTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string AllRequests = "SELECT RequestNo FROM org.RegistrationRequests ORDER BY RequestNo;";

    /// <summary>Error 33504: a write refused by a BLOCK predicate.</summary>
    private const int BlockPredicateViolation = 33504;

    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => database.SeedAsync();

    private Task<DbConnection> OpenAsAsync(TestCurrentUserContext user) =>
        new SqlConnectionFactory(database.ConnectionString, user).OpenConnectionAsync();

    private async Task<string[]> RequestsVisibleToAsync(TestCurrentUserContext user)
    {
        await using var connection = await OpenAsAsync(user);
        return [.. await connection.QueryAsync<string>(AllRequests)];
    }

    private async Task LinkToInstitutionAsync(string requestNo, int institutionId)
    {
        await using var connection = await OpenAsAsync(TestCurrentUserContext.Platform());
        var linked = await connection.ExecuteAsync(
            "UPDATE org.RegistrationRequests SET CreatedInstitutionId = @institutionId WHERE RequestNo = @requestNo;",
            new { requestNo, institutionId });
        Assert.Equal(1, linked);
    }

    // ------------------------------------------------------------------ who sees what

    [Fact]
    public async Task AnApplicantSeesOnlyTheirOwnApplication()
    {
        Assert.Equal(["REQ-ALPHA"], await RequestsVisibleToAsync(TestCurrentUserContext.ForUser(SethikaTestDatabase.AlphaUserId)));
        Assert.Equal(["REQ-BETA"], await RequestsVisibleToAsync(TestCurrentUserContext.ForUser(SethikaTestDatabase.BetaUserId)));
    }

    [Fact]
    public async Task ThePlatformSeesEveryApplication()
    {
        // The reviewer queue.
        Assert.Equal(["REQ-ALPHA", "REQ-BETA"], await RequestsVisibleToAsync(TestCurrentUserContext.Platform()));
    }

    [Fact]
    public async Task AnInstitutionSeesOnlyTheApplicationThatCreatedIt()
    {
        var alpha = TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId);
        var beta = TestCurrentUserContext.ForInstitution(database.BetaInstitutionId);

        // Before approval links it, the application is none of the institution's business.
        Assert.Empty(await RequestsVisibleToAsync(alpha));

        await LinkToInstitutionAsync("REQ-ALPHA", database.AlphaInstitutionId);

        Assert.Equal(["REQ-ALPHA"], await RequestsVisibleToAsync(alpha));
        Assert.Empty(await RequestsVisibleToAsync(beta));
    }

    [Fact]
    public async Task NobodyElseSeesAnyApplication()
    {
        // Signed in but neither applicant, nor platform, nor the institution - and signed out.
        Assert.Empty(await RequestsVisibleToAsync(TestCurrentUserContext.ForUser(Guid.CreateVersion7())));
        Assert.Empty(await RequestsVisibleToAsync(TestCurrentUserContext.Anonymous()));

        await using var unstamped = new SqlConnection(database.ConnectionString);
        await unstamped.OpenAsync();
        Assert.Empty(await unstamped.QueryAsync<string>(AllRequests));
    }

    // ------------------------------------------------------------------ who changes what

    [Fact]
    public async Task AnotherApplicantCannotChangeAnApplication()
    {
        await using (var beta = await OpenAsAsync(TestCurrentUserContext.ForUser(SethikaTestDatabase.BetaUserId)))
        {
            var changed = await beta.ExecuteAsync(
                "UPDATE org.RegistrationRequests SET ProposedName = N'Hijacked' WHERE RequestNo = 'REQ-ALPHA';");

            Assert.Equal(0, changed);
        }

        await using var platform = await OpenAsAsync(TestCurrentUserContext.Platform());
        Assert.Equal("Alpha Temple", await platform.ExecuteScalarAsync<string>(
            "SELECT ProposedName FROM org.RegistrationRequests WHERE RequestNo = 'REQ-ALPHA';"));
    }

    [Fact]
    public async Task AnApplicantCannotFileAnApplicationInSomeoneElsesName()
    {
        const string File =
            """
            INSERT org.RegistrationRequests
                (RequestNo, ApplicantUserId, InstitutionTypeId, ProposedName, OfficialRegistrationNo,
                 RegisteringAuthority, RegistrationDate, AddressLine1, City, DistrictId, Latitude, Longitude,
                 OfficePhone, ApplicantFullName, ApplicantNic, ApplicantDesignation, ApplicantPhone)
            SELECT @requestNo, @applicant, t.Id, N'Gamma Temple', @requestNo, N'Authority', '2024-01-01',
                   N'3 Gamma Road', N'Colombo', d.Id, 6.929100, 79.863200,
                   '0111000003', N'Gamma Steward', '901234569V', N'Trustee', '0773000003'
            FROM org.InstitutionTypes t CROSS JOIN core.Districts d
            WHERE t.Code = 'TEMPLE' AND d.Code = 'CMB';
            """;

        await using var beta = await OpenAsAsync(TestCurrentUserContext.ForUser(SethikaTestDatabase.BetaUserId));

        var forged = await Assert.ThrowsAsync<SqlException>(() => beta.ExecuteAsync(
            File, new { requestNo = "REQ-FORGED", applicant = SethikaTestDatabase.AlphaUserId }));
        Assert.Equal(BlockPredicateViolation, forged.Number);

        // The same insert in their own name is allowed: the predicate refuses the name, not the act.
        Assert.Equal(1, await beta.ExecuteAsync(
            File, new { requestNo = "REQ-GAMMA", applicant = SethikaTestDatabase.BetaUserId }));
    }

    [Fact]
    public async Task AnApplicantCannotHandTheirApplicationToSomeoneElse()
    {
        await using var alpha = await OpenAsAsync(TestCurrentUserContext.ForUser(SethikaTestDatabase.AlphaUserId));

        var handed = await Assert.ThrowsAsync<SqlException>(() => alpha.ExecuteAsync(
            "UPDATE org.RegistrationRequests SET ApplicantUserId = @beta WHERE RequestNo = 'REQ-ALPHA';",
            new { beta = SethikaTestDatabase.BetaUserId }));

        Assert.Equal(BlockPredicateViolation, handed.Number);
    }
}

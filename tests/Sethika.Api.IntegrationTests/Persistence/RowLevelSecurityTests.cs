using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves tenant isolation is enforced by the database, not by careful queries.
/// Every query below is a plain "give me all bookings" with no WHERE on InstitutionId -
/// if a test sees another tenant's row, Row-Level Security has failed.
/// </summary>
[Collection(nameof(RowLevelSecurityTests))]
[CollectionDefinition(nameof(RowLevelSecurityTests), DisableParallelization = true)]
public sealed class RowLevelSecurityTests(SethikaTestDatabase database)
    : IClassFixture<SethikaTestDatabase>
{
    private const string AllBookings = "SELECT BookingNo FROM booking.Bookings ORDER BY BookingNo";

    private SqlConnectionFactory FactoryFor(TestCurrentUserContext user) =>
        new(database.ConnectionString, user);

    private async Task<string[]> BookingsVisibleToAsync(TestCurrentUserContext user)
    {
        await using var connection = await FactoryFor(user).OpenConnectionAsync().ConfigureAwait(false);
        var rows = await connection.QueryAsync<string>(AllBookings).ConfigureAwait(false);
        return [.. rows];
    }

    [Fact]
    public async Task Institution_A_sees_only_its_own_booking()
    {
        var visible = await BookingsVisibleToAsync(
            TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId));

        Assert.Equal("BK-ALPHA", Assert.Single(visible));
    }

    [Fact]
    public async Task Institution_B_sees_only_its_own_booking()
    {
        var visible = await BookingsVisibleToAsync(
            TestCurrentUserContext.ForInstitution(database.BetaInstitutionId));

        Assert.Equal("BK-BETA", Assert.Single(visible));
    }

    [Fact]
    public async Task Platform_context_sees_every_tenant()
    {
        var visible = await BookingsVisibleToAsync(TestCurrentUserContext.Platform());

        Assert.Equal(["BK-ALPHA", "BK-BETA"], visible);
    }

    [Fact]
    public async Task Anonymous_context_sees_nothing()
    {
        var visible = await BookingsVisibleToAsync(TestCurrentUserContext.Anonymous());

        Assert.Empty(visible);
    }

    /// <summary>
    /// The secure default: a connection nobody stamped shows no rows at all, so forgetting
    /// to set the context can never widen access.
    /// </summary>
    [Fact]
    public async Task Unstamped_connection_sees_nothing()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var visible = await connection.QueryAsync<string>(AllBookings);

        Assert.Empty(visible);
    }

    /// <summary>
    /// The security property behind @read_only = 1: once stamped, nothing later in the
    /// request - including injected SQL - can re-point the connection at another tenant.
    /// </summary>
    [Fact]
    public async Task Stamped_context_cannot_be_reassigned_to_another_tenant()
    {
        await using var connection = await FactoryFor(
            TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId))
            .OpenConnectionAsync();

        var escalate = async () => await connection.ExecuteAsync(
            "EXEC sys.sp_set_session_context @key = N'InstitutionId', @value = @other;",
            new { other = database.BetaInstitutionId });

        var error = await Assert.ThrowsAsync<SqlException>(escalate);
        Assert.Contains("read_only", error.Message, StringComparison.OrdinalIgnoreCase);

        // and the original tenant is still the one in force
        var visible = await connection.QueryAsync<string>(AllBookings);
        Assert.Equal("BK-ALPHA", Assert.Single(visible));
    }

    /// <summary>
    /// Connections are pooled and the stamp is read-only, so a leaked stamp would either
    /// show the wrong tenant or throw on restamp. Reusing the pool as a different tenant
    /// must simply work.
    /// </summary>
    [Fact]
    public async Task Pooled_connections_do_not_leak_context_between_tenants()
    {
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("BK-ALPHA", Assert.Single(await BookingsVisibleToAsync(
                TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId))));

            Assert.Equal("BK-BETA", Assert.Single(await BookingsVisibleToAsync(
                TestCurrentUserContext.ForInstitution(database.BetaInstitutionId))));
        }
    }

    /// <summary>
    /// The EF Core write path goes through a DbConnectionInterceptor rather than the
    /// factory, so it is stamped separately and verified separately.
    /// </summary>
    [Fact]
    public async Task Ef_core_connections_are_stamped_by_the_interceptor()
    {
        var user = TestCurrentUserContext.ForInstitution(database.BetaInstitutionId);

        var options = new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options;

        await using var context = new SethikaDbContext(options);

        var stamped = await context.Database
            .SqlQuery<int?>($"SELECT CAST(SESSION_CONTEXT(N'InstitutionId') AS int) AS Value")
            .SingleAsync();

        Assert.Equal(database.BetaInstitutionId, stamped);
    }
}

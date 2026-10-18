using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Onboarding;

/// <summary>Plan 02, B6: what the decision locks hold, and for how long.</summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class RegistrationLocksTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string RequestNo = "REQ-TEST-000201";

    /// <summary>Error 1222: a lock request that waited longer than <c>LOCK_TIMEOUT</c>.</summary>
    private const int LockTimeout = 1222;

    private readonly OnboardingScenario _scenario = new(database);

    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => database.SeedAsync();

    private async Task<SqlConnection> OpenAsPlatformAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    /// <summary>A reviewer's change to the application, giving up after half a second.</summary>
    private const string ReviewerChange =
        "SET LOCK_TIMEOUT 500; UPDATE org.RegistrationRequests SET UpdatedAt = SYSUTCDATETIME() WHERE RequestNo = @RequestNo;";

    [Fact]
    public async Task AClaimHoldsTheApplicationUntilTheDecisionEnds()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/88");
        await using var reviewer = await OpenAsPlatformAsync();

        await using (var decision = _scenario.AsReviewer())
        {
            await using var transaction = await decision.Database.BeginTransactionAsync();
            Assert.True(await new SqlRegistrationLocks(decision).ClaimForDecisionAsync(RequestNo));

            // While the decision is open, the reviewer's change waits rather than slipping under it.
            var blocked = await Assert.ThrowsAsync<SqlException>(() => reviewer.ExecuteAsync(ReviewerChange, new { RequestNo }));
            Assert.Equal(LockTimeout, blocked.Number);

            await transaction.RollbackAsync();
        }

        Assert.Equal(1, await reviewer.ExecuteAsync(ReviewerChange, new { RequestNo }));
    }

    [Fact]
    public async Task AClaimHoldsTheDecisionLockSoDecisionsTakeTurns()
    {
        await _scenario.ReadyToApproveAsync(RequestNo, "BUD/GAL/1952/88");
        await using var other = await OpenAsPlatformAsync();
        const string TryLock =
            """
            BEGIN TRANSACTION;
            DECLARE @granted INT;
            EXEC @granted = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            ROLLBACK;
            SELECT @granted;
            """;

        await using var decision = _scenario.AsReviewer();
        await using var transaction = await decision.Database.BeginTransactionAsync();
        Assert.True(await new SqlRegistrationLocks(decision).ClaimForDecisionAsync(RequestNo));

        // A negative answer is "not granted" - here, timed out at once.
        Assert.True(await other.ExecuteScalarAsync<int>(TryLock, new { resource = SqlRegistrationLocks.DecisionLock }) < 0);

        await transaction.RollbackAsync();
        Assert.True(await other.ExecuteScalarAsync<int>(TryLock, new { resource = SqlRegistrationLocks.DecisionLock }) >= 0);
    }

    [Fact]
    public async Task AnUnknownApplicationIsNotClaimed()
    {
        await using var decision = _scenario.AsReviewer();
        await using var transaction = await decision.Database.BeginTransactionAsync();

        Assert.False(await new SqlRegistrationLocks(decision).ClaimForDecisionAsync("REQ-TEST-999999"));
    }

    [Fact]
    public async Task AClaimOutsideATransactionIsRefused()
    {
        await using var decision = _scenario.AsReviewer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlRegistrationLocks(decision).ClaimForDecisionAsync(RequestNo));
    }
}

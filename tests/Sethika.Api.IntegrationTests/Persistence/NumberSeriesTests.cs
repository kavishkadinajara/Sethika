using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Persistence;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Plan 02, B5: <c>core.NumberSeries</c> numbers are gap-free and never handed out twice, against
/// the real table. Each test uses a series code of its own, so none of them sees another's rows.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class NumberSeriesTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const int Year = 2026;
    private const int Callers = 6;

    private readonly NumberSeriesDefinition _series =
        new("T" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)), "TST", 6);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM core.NumberSeries WHERE SeriesCode = @code;", new { code = _series.Code });
    }

    private SethikaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.Platform()))
            .Options);

    /// <summary>One caller: its own context and transaction, committed or rolled back.</summary>
    private async Task<string> TakeAsync(bool commit = true, int year = Year)
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var number = await new SqlNumberSeries(context).NextAsync(_series, NumberSeriesDefinition.PlatformScope, year);

        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        return number;
    }

    private static string Formatted(int number, int year = Year) => $"TST-{year}-{number:D6}";

    // ------------------------------------------------------------------ numbering

    [Fact]
    public async Task NumbersCountUpFromOneAndAreFormattedWithTheYear()
    {
        Assert.Equal(Formatted(1), await TakeAsync());
        Assert.Equal(Formatted(2), await TakeAsync());
        Assert.Equal(Formatted(3), await TakeAsync());
    }

    [Fact]
    public async Task EachYearStartsAgainAtOne()
    {
        Assert.Equal(Formatted(1), await TakeAsync());
        Assert.Equal(Formatted(2), await TakeAsync());
        Assert.Equal(Formatted(1, Year + 1), await TakeAsync(year: Year + 1));
    }

    [Fact]
    public async Task ARolledBackCallerGivesItsNumberBack()
    {
        // Both ways a number is issued: the row's first insert, and a later increment.
        Assert.Equal(Formatted(1), await TakeAsync(commit: false));
        Assert.Equal(Formatted(1), await TakeAsync());

        Assert.Equal(Formatted(2), await TakeAsync(commit: false));
        Assert.Equal(Formatted(2), await TakeAsync());
    }

    [Fact]
    public async Task ANumberIsNeverTakenOutsideATransaction()
    {
        await using var context = NewContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlNumberSeries(context).NextAsync(_series, NumberSeriesDefinition.PlatformScope, Year));

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM core.NumberSeries WHERE SeriesCode = @code;", new { code = _series.Code }));
    }

    // ------------------------------------------------------------------ concurrency, forced

    [Fact]
    public async Task ConcurrentFirstCallersOfANewSeriesGetDistinctConsecutiveNumbers()
    {
        // No row yet. The holder locks the missing key's range, every caller queues behind it, and on
        // release they all run against the same empty key: the case where an unserialised MERGE has
        // two callers insert and one fail on the primary key.
        var results = await RaceAsync(
            "SELECT LastNumber FROM core.NumberSeries WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE SeriesCode = @code AND ScopeKey = 'PLATFORM' AND SeriesYear = @year;");

        Assert.Equal(Enumerable.Range(1, Callers).Select(n => Formatted(n)), results.Order());
    }

    [Fact]
    public async Task ConcurrentCallersOfAnExistingSeriesGetDistinctConsecutiveNumbers()
    {
        Assert.Equal(Formatted(1), await TakeAsync());

        var results = await RaceAsync(
            "SELECT LastNumber FROM core.NumberSeries WITH (UPDLOCK) " +
            "WHERE SeriesCode = @code AND ScopeKey = 'PLATFORM' AND SeriesYear = @year;");

        Assert.Equal(Enumerable.Range(2, Callers).Select(n => Formatted(n)), results.Order());
    }

    [Fact]
    public async Task FirstCallersReleasedTogetherNeverCollideOnTheInsert()
    {
        // The window HOLDLOCK closes is inside one MERGE, between finding no row and inserting one, so
        // no lock held from outside can stop a caller there. Instead every caller opens its transaction
        // first and they are released at once, many times over, each round on a new series.
        const int Rounds = 40;
        var codes = new List<string>();

        try
        {
            for (var round = 0; round < Rounds; round++)
            {
                var series = new NumberSeriesDefinition("T" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)), "TST", 6);
                codes.Add(series.Code);

                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var ready = new CountdownEvent(Callers);
                var callers = Enumerable.Range(0, Callers).Select(_ => Task.Run(async () =>
                {
                    await using var context = NewContext();
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    ready.Signal();
                    await gate.Task;

                    var number = await new SqlNumberSeries(context).NextAsync(series, NumberSeriesDefinition.PlatformScope, Year);
                    await transaction.CommitAsync();
                    return number;
                })).ToArray();

                ready.Wait(TimeSpan.FromSeconds(30));
                gate.SetResult();

                var results = await Task.WhenAll(callers);
                Assert.Equal(Enumerable.Range(1, Callers).Select(n => Formatted(n)), results.Order());
            }
        }
        finally
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.ExecuteAsync("DELETE FROM core.NumberSeries WHERE SeriesCode IN @codes;", new { codes });
        }
    }

    /// <summary>
    /// Holds <paramref name="holdSql"/>'s lock, starts every caller, waits until all of them are
    /// queued on <c>core.NumberSeries</c>, then lets them go at once.
    /// </summary>
    private async Task<string[]> RaceAsync(string holdSql)
    {
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(holdSql, new { code = _series.Code, year = Year }, hold);

        var callers = Enumerable.Range(0, Callers).Select(_ => Task.Run(() => TakeAsync())).ToArray();

        await WaitForQueuedCallersAsync(Callers);
        await hold.RollbackAsync();

        return await Task.WhenAll(callers);
    }

    private async Task WaitForQueuedCallersAsync(int expected)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            var waiting = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.resource_database_id = DB_ID()
                  AND l.request_status = 'WAIT'
                  AND p.object_id = OBJECT_ID(N'core.NumberSeries');
                """);

            if (waiting >= expected)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Only {waiting} of {expected} callers queued on the series.");
            await Task.Delay(50);
        }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Bookings.Engine;
using Sethika.Domain.Bookings;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// The booking engine (plan 03, 5.3) against real SQL Server and Redis, with every race forced
/// rather than hoped for.
/// </summary>
/// <remarks>
/// Parallel requests rarely overlap on their own: the first finishes before the rest have read
/// anything. So each concurrency test holds a door shut, waits until every caller is queued behind
/// it, and only then opens it.
/// <list type="bullet">
/// <item>With the Redis lock, the door is the lock key itself, set by the test.</item>
/// <item>Without it (Redis down, C2), the door is an update lock on the slot-day's row, taken from a
/// separate connection: the callers' reads pass it, their conditional UPDATEs queue behind it, and
/// <c>sys.dm_tran_locks</c> shows when all of them are waiting.</item>
/// </list>
/// An update lock, not an exclusive one: it leaves the readers alone whether or not the database runs
/// with READ_COMMITTED_SNAPSHOT (it does, as Azure SQL does).
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class BookingEngineTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private readonly List<Guid> _donors = [];
    private ConnectionMultiplexer _redis = null!;

    /// <summary>Ten days ahead in Colombo: inside every slot's advance window here.</summary>
    private static DateOnly SlotDay => SriLankaTime.Today(DateTime.UtcNow).AddDays(10);

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        await using (var connection = await PlatformConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                DELETE h FROM booking.BookingStatusHistory h JOIN booking.Bookings b ON b.Id = h.BookingId WHERE b.DonorUserId IN @donors;
                DELETE FROM booking.Bookings WHERE DonorUserId IN @donors;
                DELETE FROM auth.Users WHERE Id IN @donors;
                """,
                new { donors = _donors });
        }

        await _redis.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private SethikaDbContext ContextFor(Guid userId) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(TestCurrentUserContext.ForUser(userId)))
            .Options);

    private RedisSlotLocks RedisLocks(SlotLockOptions? options = null) =>
        new(_redis, Options.Create(options ?? new SlotLockOptions()), TimeProvider.System, NullLogger<RedisSlotLocks>.Instance);

    /// <summary>What the engine sees when Redis is down: every attempt goes on without the lock.</summary>
    private sealed class NoLocks : ISlotLocks
    {
        public Task<ILockLease> AcquireAsync(int mealSlotId, DateOnly slotDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ILockLease>(new Unlocked());

        private sealed class Unlocked : ILockLease
        {
            public LockOutcome Outcome => LockOutcome.Unavailable;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>A donor with a confirmed phone, removed again in <see cref="DisposeAsync"/>.</summary>
    private async Task<Guid> DonorAsync()
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Engine Test Donor', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, phone });

        _donors.Add(id);
        return id;
    }

    private async Task<Guid[]> DonorsAsync(int count)
    {
        var donors = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            donors[i] = await DonorAsync();
        }

        return donors;
    }

    /// <summary>A meal slot in Alpha, every day, approval needed. Returns its internal and public ids.</summary>
    private async Task<(int Id, Guid PublicId)> SlotAsync(byte maxSponsors = 1, short maxHeadcount = 100)
    {
        var publicId = Guid.CreateVersion7();

        await using var connection = await PlatformConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT booking.MealSlots (PublicId, InstitutionId, Name, StartTime, EndTime, BookingMode, MaxSponsors,
                                      MinHeadcount, MaxHeadcount, MinAdvanceDays, MaxAdvanceDays, CreatedBy)
            VALUES (@publicId, @institutionId, @name, '11:00', '12:30', @mode, @maxSponsors, 1, @maxHeadcount, 1, 60, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId,
                institutionId = database.AlphaInstitutionId,
                name = "Engine slot " + publicId.ToString("N")[..12],
                mode = maxSponsors == 1 ? BookingMode.Exclusive : BookingMode.Shared,
                maxSponsors,
                maxHeadcount,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });

        return (id, publicId);
    }

    /// <summary>The slot-day row, made ahead so a test can lock it before any caller arrives.</summary>
    private async Task<long> InstanceAsync(int slotId, DateOnly date)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT booking.SlotInstances (InstitutionId, MealSlotId, SlotDate, MaxSponsors, MaxHeadcount)
            SELECT InstitutionId, Id, @date, MaxSponsors, MaxHeadcount FROM booking.MealSlots WHERE Id = @slotId;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) });
    }

    private async Task<BookMealResult> BookAsync(Guid donor, Guid slot, DateOnly date, short headcount, ISlotLocks locks)
    {
        await using var context = ContextFor(donor);
        var handler = new BookMealHandler(context, locks, new SqlSlotInventory(context), new SqlNumberSeries(context), TimeProvider.System);
        return await handler.BookAsync(donor, new BookMealCommand(slot, date, headcount, FulfilmentMode.DonorBrings, null, null));
    }

    private async Task<BookingDecisionResult> CancelAsync(Guid donor, string bookingNo)
    {
        await using var context = ContextFor(donor);
        return await new BookingDecisionHandler(context, new SqlSlotInventory(context), TimeProvider.System)
            .CancelAsDonorAsync(bookingNo, "Plans changed", donor);
    }

    private sealed record Counters(byte BookedSponsors, short BookedHeadcount, string Status, int Instances, int ActiveBookings);

    private async Task<Counters> CountersAsync(int slotId, DateOnly date)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.QuerySingleAsync<Counters>(
            """
            SELECT ISNULL(MAX(i.BookedSponsors), 0)  AS BookedSponsors,
                   ISNULL(MAX(i.BookedHeadcount), 0) AS BookedHeadcount,
                   ISNULL(MAX(i.Status), '')         AS Status,
                   COUNT(DISTINCT i.Id)              AS Instances,
                   (SELECT COUNT(*) FROM booking.Bookings b JOIN booking.SlotInstances x ON x.Id = b.SlotInstanceId
                    WHERE x.MealSlotId = @slotId AND x.SlotDate = @date AND (b.Status = 'Requested' OR b.Status = 'Approved')) AS ActiveBookings
            FROM booking.SlotInstances i
            WHERE i.MealSlotId = @slotId AND i.SlotDate = @date;
            """,
            new { slotId, date = date.ToDateTime(TimeOnly.MinValue) });
    }

    /// <summary>Waits until <paramref name="expected"/> requests are queued on locks of <paramref name="table"/>.</summary>
    private static async Task WaitForWaitersAsync(SqlConnection observer, string table, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        int waiting;
        do
        {
            waiting = await observer.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.dm_tran_locks l
                JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = 'WAIT' AND l.resource_database_id = DB_ID() AND p.object_id = OBJECT_ID(@table);
                """,
                new { table });

            if (waiting >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Only {waiting} of {expected} callers queued on {table}: the race was not forced.");
    }

    /// <summary>
    /// Runs <paramref name="donors"/> bookings at once on a slot-day whose row is update-locked from
    /// another connection, releasing it only when every one of them is queued on it.
    /// </summary>
    private async Task<BookMealResult[]> RaceOnTheRowAsync(Guid slot, long instanceId, Guid[] donors, short headcount)
    {
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM booking.SlotInstances WITH (UPDLOCK, ROWLOCK) WHERE Id = @instanceId;", new { instanceId }, hold);

        var attempts = donors.Select(d => Task.Run(() => BookAsync(d, slot, SlotDay, headcount, new NoLocks()))).ToArray();

        await using (var observer = new SqlConnection(database.ConnectionString))
        {
            await observer.OpenAsync();
            await WaitForWaitersAsync(observer, "booking.SlotInstances", donors.Length);
        }

        await hold.CommitAsync();
        return await Task.WhenAll(attempts);
    }

    /// <summary>
    /// Runs <paramref name="donors"/> bookings at once behind the slot-day's Redis lock, held by the
    /// test until all of them are trying for it.
    /// </summary>
    private async Task<BookMealResult[]> RaceOnTheLockAsync(int slotId, Guid slot, Guid[] donors, short headcount)
    {
        var key = RedisSlotLocks.KeyFor(slotId, SlotDay);
        var redis = _redis.GetDatabase();
        Assert.True(await redis.StringSetAsync(key, "held-by-the-test", TimeSpan.FromSeconds(30), When.NotExists));

        var locks = RedisLocks(new SlotLockOptions { WaitTimeout = TimeSpan.FromSeconds(30) });
        var attempts = donors.Select(d => Task.Run(() => BookAsync(d, slot, SlotDay, headcount, locks))).ToArray();

        // Nothing in Redis says who is waiting, so give every caller time to reach its first try.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.All(attempts, a => Assert.False(a.IsCompleted));

        await redis.KeyDeleteAsync(key);
        return await Task.WhenAll(attempts);
    }

    private static void AssertOutcomes(BookMealResult[] results, int booked)
    {
        Assert.Equal(booked, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(BookingErrors.SlotFull, r.Error));
        Assert.Equal(booked, results.Where(r => r.Succeeded).Select(r => r.BookingNo).Distinct().Count());
    }

    // ------------------------------------------------------------------ one booking

    [Fact]
    public async Task ABookingTakesAPlaceAndIsNumberedFromTheBookingSeries()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);
        var donor = await DonorAsync();

        var result = await BookAsync(donor, slot, SlotDay, 12, RedisLocks());

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(BookingStatus.Requested, result.Status);
        Assert.Matches(@"^BK-\d{4}-\d{6}$", result.BookingNo);
        Assert.Equal(new Counters(1, 12, SlotInstanceStatus.Open, 1, 1), await CountersAsync(slotId, SlotDay));

        await using var connection = await PlatformConnectionAsync();
        var trail = await connection.QuerySingleAsync<(string ToStatus, string EventType)>(
            """
            SELECT h.ToStatus AS ToStatus, o.EventType AS EventType
            FROM booking.Bookings b
            JOIN booking.BookingStatusHistory h ON h.BookingId = b.Id
            JOIN core.OutboxMessages o ON o.AggregateType = 'Booking' AND o.AggregateId = b.BookingNo
            WHERE b.BookingNo = @bookingNo;
            """,
            new { bookingNo = result.BookingNo });

        Assert.Equal((BookingStatus.Requested, BookMealHandler.RequestedEvent), trail);
    }

    [Fact]
    public async Task TheLastPlaceMarksTheSlotDayFull()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 2);
        var donors = await DonorsAsync(3);

        Assert.True((await BookAsync(donors[0], slot, SlotDay, 5, RedisLocks())).Succeeded);
        Assert.Equal(SlotInstanceStatus.Open, (await CountersAsync(slotId, SlotDay)).Status);

        Assert.True((await BookAsync(donors[1], slot, SlotDay, 5, RedisLocks())).Succeeded);
        Assert.Equal(SlotInstanceStatus.Full, (await CountersAsync(slotId, SlotDay)).Status);

        Assert.Equal(BookingErrors.SlotFull, (await BookAsync(donors[2], slot, SlotDay, 5, RedisLocks())).Error);
    }

    [Fact]
    public async Task TheSameDonorCannotHoldTheSlotDayTwice_AndTheSecondTryGivesItsPlaceBack()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);
        var donor = await DonorAsync();

        Assert.True((await BookAsync(donor, slot, SlotDay, 5, RedisLocks())).Succeeded);
        Assert.Equal(BookingErrors.AlreadyBooked, (await BookAsync(donor, slot, SlotDay, 5, RedisLocks())).Error);

        // The second attempt took a place before UX_Bookings_DonorPerSlot refused its booking; the
        // rollback has to have given it back.
        Assert.Equal(new Counters(1, 5, SlotInstanceStatus.Open, 1, 1), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task ACancelledPlaceIsFreeForTheNextDonor()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);
        var (first, second) = (await DonorAsync(), await DonorAsync());

        var booked = await BookAsync(first, slot, SlotDay, 8, RedisLocks());
        Assert.True(booked.Succeeded);
        Assert.Equal(BookingErrors.SlotFull, (await BookAsync(second, slot, SlotDay, 8, RedisLocks())).Error);

        Assert.True((await CancelAsync(first, booked.BookingNo!)).Succeeded);
        Assert.Equal(new Counters(0, 0, SlotInstanceStatus.Open, 1, 0), await CountersAsync(slotId, SlotDay));

        Assert.True((await BookAsync(second, slot, SlotDay, 8, RedisLocks())).Succeeded);
        Assert.Equal(new Counters(1, 8, SlotInstanceStatus.Full, 1, 1), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task ACancelledBookingCannotBeCancelledAgain_SoItsPlaceIsGivenBackOnce()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);
        var (first, second) = (await DonorAsync(), await DonorAsync());

        var booked = await BookAsync(first, slot, SlotDay, 8, RedisLocks());
        Assert.True((await BookAsync(second, slot, SlotDay, 8, RedisLocks())).Succeeded);

        Assert.True((await CancelAsync(first, booked.BookingNo!)).Succeeded);
        Assert.Equal(BookingErrors.InvalidTransition, (await CancelAsync(first, booked.BookingNo!)).Error);

        Assert.Equal(new Counters(1, 8, SlotInstanceStatus.Open, 1, 1), await CountersAsync(slotId, SlotDay));
    }

    // ------------------------------------------------------------------ races behind the Redis lock

    [Fact]
    public async Task WithTheLock_TwentyDonorsAtOnceGetOneExclusiveSlot()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);

        var results = await RaceOnTheLockAsync(slotId, slot, await DonorsAsync(20), 10);

        AssertOutcomes(results, booked: 1);
        Assert.Equal(new Counters(1, 10, SlotInstanceStatus.Full, 1, 1), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task WithTheLock_TenDonorsAtOnceGetFourSharedPlaces()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);

        var results = await RaceOnTheLockAsync(slotId, slot, await DonorsAsync(10), 10);

        AssertOutcomes(results, booked: 4);
        Assert.Equal(new Counters(4, 40, SlotInstanceStatus.Full, 1, 4), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task WithTheLock_TheHeadcountRunsOutBeforeTheSponsorPlaces()
    {
        // Four sponsor places but room for 25 people: two bookings of ten fit, a third does not.
        var (slotId, slot) = await SlotAsync(maxSponsors: 4, maxHeadcount: 25);

        var results = await RaceOnTheLockAsync(slotId, slot, await DonorsAsync(6), 10);

        AssertOutcomes(results, booked: 2);
        Assert.Equal(new Counters(2, 20, SlotInstanceStatus.Open, 1, 2), await CountersAsync(slotId, SlotDay));
    }

    // ------------------------------------------------------------------ the same races with Redis down (C2)

    [Fact]
    public async Task WithoutTheLock_TwentyDonorsAtOnceStillGetOneExclusiveSlot()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 1);
        var instance = await InstanceAsync(slotId, SlotDay);

        var results = await RaceOnTheRowAsync(slot, instance, await DonorsAsync(20), 10);

        AssertOutcomes(results, booked: 1);
        Assert.Equal(new Counters(1, 10, SlotInstanceStatus.Full, 1, 1), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task WithoutTheLock_TenDonorsAtOnceStillGetFourSharedPlaces()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);
        var instance = await InstanceAsync(slotId, SlotDay);

        var results = await RaceOnTheRowAsync(slot, instance, await DonorsAsync(10), 10);

        AssertOutcomes(results, booked: 4);
        Assert.Equal(new Counters(4, 40, SlotInstanceStatus.Full, 1, 4), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task WithoutTheLock_TheHeadcountStillRunsOutBeforeTheSponsorPlaces()
    {
        var (slotId, slot) = await SlotAsync(maxSponsors: 4, maxHeadcount: 25);
        var instance = await InstanceAsync(slotId, SlotDay);

        var results = await RaceOnTheRowAsync(slot, instance, await DonorsAsync(6), 10);

        AssertOutcomes(results, booked: 2);
        Assert.Equal(new Counters(2, 20, SlotInstanceStatus.Open, 1, 2), await CountersAsync(slotId, SlotDay));
    }

    [Fact]
    public async Task WithoutTheLock_TwoFirstBookingsOfADayMakeOneSlotDay()
    {
        // C3. Both callers must find the day missing, and both must then try to create it. The door is
        // an uncommitted insert of that very row from another connection: the callers' look-up of the
        // day queues on its key. Rolled back, the row was never there, so both look-ups answer
        // "missing" at once and both insert. One wins; the other meets UQ_SlotInst_SlotDate once the
        // winner commits, and has to read that as "it exists now", not as a failure.
        var (slotId, slot) = await SlotAsync(maxSponsors: 4);
        var donors = await DonorsAsync(2);

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            """
            INSERT booking.SlotInstances (InstitutionId, MealSlotId, SlotDate, MaxSponsors, MaxHeadcount)
            SELECT InstitutionId, Id, @date, MaxSponsors, MaxHeadcount FROM booking.MealSlots WHERE Id = @slotId;
            """,
            new { slotId, date = SlotDay.ToDateTime(TimeOnly.MinValue) },
            hold);

        var attempts = donors.Select(d => Task.Run(() => BookAsync(d, slot, SlotDay, 10, new NoLocks()))).ToArray();

        await using (var observer = new SqlConnection(database.ConnectionString))
        {
            await observer.OpenAsync();
            await WaitForWaitersAsync(observer, "booking.SlotInstances", 2);
        }

        await hold.RollbackAsync();
        var results = await Task.WhenAll(attempts);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        Assert.Equal(new Counters(2, 20, SlotInstanceStatus.Open, 1, 2), await CountersAsync(slotId, SlotDay));
    }

    // ------------------------------------------------------------------ the lock itself

    [Fact]
    public async Task ALockThatOutlivedItsHolderIsNotReleasedByThem()
    {
        // The token check: a holder slower than the lock's lifetime must not delete the lock the next
        // caller took after it expired.
        var shortLived = RedisLocks(new SlotLockOptions { Lifetime = TimeSpan.FromMilliseconds(300) });
        var slotId = RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue);
        var key = RedisSlotLocks.KeyFor(slotId, SlotDay);

        var slow = await shortLived.AcquireAsync(slotId, SlotDay);
        Assert.Equal(LockOutcome.Acquired, slow.Outcome);

        await using var next = await RedisLocks().AcquireAsync(slotId, SlotDay);   // waits out the 300 ms
        Assert.Equal(LockOutcome.Acquired, next.Outcome);

        await slow.DisposeAsync();

        Assert.True(await _redis.GetDatabase().KeyExistsAsync(key));
    }

    [Fact]
    public async Task ABusyLockAnswersBusyAfterTheWait_AndAReleasedOneIsFreeAtOnce()
    {
        var locks = RedisLocks(new SlotLockOptions { WaitTimeout = TimeSpan.FromMilliseconds(200) });
        var slotId = RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue);

        await using (var held = await locks.AcquireAsync(slotId, SlotDay))
        {
            Assert.Equal(LockOutcome.Acquired, held.Outcome);
            await using var blocked = await locks.AcquireAsync(slotId, SlotDay);
            Assert.Equal(LockOutcome.Busy, blocked.Outcome);
        }

        await using var after = await locks.AcquireAsync(slotId, SlotDay);
        Assert.Equal(LockOutcome.Acquired, after.Outcome);
    }

    [Fact]
    public async Task WithRedisUnreachableTheLockReportsUnavailable_AndTheBookingStillGoesThrough()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        await using var deadRedis = await ConnectionMultiplexer.ConnectAsync(options);
        var locks = new RedisSlotLocks(deadRedis, Options.Create(new SlotLockOptions()), TimeProvider.System, NullLogger<RedisSlotLocks>.Instance);

        await using (var attempt = await locks.AcquireAsync(1, SlotDay))
        {
            Assert.Equal(LockOutcome.Unavailable, attempt.Outcome);
        }

        var (slotId, slot) = await SlotAsync();
        var result = await BookAsync(await DonorAsync(), slot, SlotDay, 10, locks);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, (await CountersAsync(slotId, SlotDay)).BookedSponsors);
    }
}

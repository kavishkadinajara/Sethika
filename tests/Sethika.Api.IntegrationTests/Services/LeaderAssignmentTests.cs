using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Services.Appointments;
using Sethika.Domain.Bookings;
using Sethika.Domain.Services;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Services;

/// <summary>
/// Assigning religious leaders (Phase E, 8.6) against real SQL Server and Redis: a leader is never
/// in two places at once, with or without the lock, and every race is forced rather than hoped for.
/// </summary>
/// <remarks>
/// The doors, as in <c>BookingEngineTests</c>:
/// <list type="bullet">
/// <item>Without the lock, an update lock on the appointments' rows from a separate connection. The
/// handlers' reads pass it, so both see the leader free; their updates queue behind it, and
/// <c>sys.dm_tran_locks</c> shows when both wait.</item>
/// <item>With the lock, the leader's Redis key, set by the test.</item>
/// </list>
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class LeaderAssignmentTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private readonly List<Guid> _donors = [];
    private readonly List<int> _types = [];
    private ConnectionMultiplexer _redis = null!;

    /// <summary>Ten days ahead, 10:00 in Colombo (04:30 UTC): inside every type's window here.</summary>
    private static DateTime TenOClock =>
        DateTime.SpecifyKind(SriLankaTime.Today(DateTime.UtcNow).AddDays(10).ToDateTime(new TimeOnly(4, 30)), DateTimeKind.Utc);

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
                DECLARE @appointments TABLE (Id BIGINT, No VARCHAR(20));
                INSERT @appointments SELECT Id, AppointmentNo FROM booking.ServiceAppointments WHERE ServiceTypeId IN @types;
                DELETE FROM booking.LeaderTimeBlocks WHERE LeaderId IN (SELECT Id FROM org.ReligiousLeaders WHERE DisplayName LIKE N'Assign Test %');
                DELETE FROM booking.AppointmentLeaders WHERE AppointmentId IN (SELECT Id FROM @appointments);
                DELETE FROM booking.AppointmentStatusHistory WHERE AppointmentId IN (SELECT Id FROM @appointments);
                DELETE FROM core.OutboxMessages WHERE AggregateType = 'ServiceAppointment' AND AggregateId IN (SELECT No FROM @appointments);
                DELETE FROM booking.ServiceAppointments WHERE Id IN (SELECT Id FROM @appointments);
                DELETE FROM org.LeaderTimeOff WHERE LeaderId IN (SELECT Id FROM org.ReligiousLeaders WHERE DisplayName LIKE N'Assign Test %');
                DELETE FROM org.ReligiousLeaders WHERE DisplayName LIKE N'Assign Test %';
                DELETE FROM booking.ServiceTypes WHERE Id IN @types;
                DELETE FROM auth.Users WHERE Id IN @donors;
                """,
                new { types = _types.DefaultIfEmpty(-1), donors = _donors.DefaultIfEmpty(Guid.Empty) });
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

    /// <summary>Alpha's owner, acting for Alpha.</summary>
    private SethikaDbContext AlphaContext() =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(
                new TestCurrentUserContext { UserId = SethikaTestDatabase.AlphaUserId, InstitutionId = database.AlphaInstitutionId }))
            .Options);

    private RedisLeaderLocks RedisLocks(TimeSpan? wait = null) =>
        new(_redis, Options.Create(new LeaderLockOptions { WaitTimeout = wait ?? TimeSpan.FromSeconds(5) }), TimeProvider.System, NullLogger<RedisLeaderLocks>.Instance);

    /// <summary>What the handler sees when Redis is down: every attempt goes on without the lock.</summary>
    private sealed class NoLocks : ILeaderLocks
    {
        public Task<ILockLease> AcquireAsync(IEnumerable<int> leaderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<ILockLease>(new Unlocked());

        private sealed class Unlocked : ILockLease
        {
            public LockOutcome Outcome => LockOutcome.Unavailable;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private async Task<AppointmentResult> AssignAsync(string appointmentNo, ILeaderLocks locks, params Guid[] leaders)
    {
        await using var context = AlphaContext();
        return await new AssignLeadersHandler(context, locks, TimeProvider.System)
            .AssignAsync(database.AlphaInstitutionId, appointmentNo, leaders, SethikaTestDatabase.AlphaUserId);
    }

    private async Task<int> ServiceTypeAsync(short durationMinutes = 60, short bufferMinutes = 0, byte leadersRequired = 1, int? institutionId = null)
    {
        await using var connection = await PlatformConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT booking.ServiceTypes (PublicId, InstitutionId, Name, DurationMinutes, BufferMinutes, LeadersRequired, LocationMode, CreatedBy)
            VALUES (@publicId, @institutionId, @name, @durationMinutes, @bufferMinutes, @leadersRequired, 'Either', @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new
            {
                publicId = Guid.CreateVersion7(),
                institutionId = institutionId ?? database.AlphaInstitutionId,
                name = "Assign Test " + Guid.NewGuid().ToString("N")[..12],
                durationMinutes,
                bufferMinutes,
                leadersRequired,
                createdBy = SethikaTestDatabase.AlphaUserId,
            });
        _types.Add(id);
        return id;
    }

    /// <summary>A leader on Alpha's roster (or another institution's). Returns the internal and public ids.</summary>
    private async Task<(int Id, Guid PublicId)> LeaderAsync(string name = "Ven. A", int? institutionId = null, bool active = true)
    {
        var publicId = Guid.CreateVersion7();
        await using var connection = await PlatformConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT org.ReligiousLeaders (PublicId, InstitutionId, DisplayName, IsActive, CreatedBy)
            VALUES (@publicId, @institutionId, @name, @active, @createdBy);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new { publicId, institutionId = institutionId ?? database.AlphaInstitutionId, name = "Assign Test " + name, active, createdBy = SethikaTestDatabase.AlphaUserId });
        return (id, publicId);
    }

    private async Task<Guid> DonorAsync()
    {
        var id = Guid.CreateVersion7();
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Assign Test Donor', @phone, 1, 'en', 'Active', 'stamp');
            """,
            new { id, phone });
        _donors.Add(id);
        return id;
    }

    /// <summary>A requested appointment of <paramref name="typeId"/> from <paramref name="startsAt"/>. Returns its id and number.</summary>
    private async Task<(long Id, string No)> AppointmentAsync(int typeId, DateTime startsAt, string location = ServiceLocation.AtInstitution, Guid? donorId = null)
    {
        var donor = donorId ?? await DonorAsync();
        var no = "APT-T-" + RandomNumberGenerator.GetHexString(12);
        await using var connection = await PlatformConnectionAsync();
        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT booking.ServiceAppointments (AppointmentNo, InstitutionId, ServiceTypeId, DonorUserId, DonorName, DonorPhone,
                                                StartsAt, EndsAt, LocationMode, Address, CreatedBy)
            SELECT @no, InstitutionId, Id, @donor, N'Assign Test Donor', '+94770000000',
                   @startsAt, DATEADD(MINUTE, DurationMinutes, @startsAt), @location,
                   CASE WHEN @location = 'AtDonorPlace' THEN N'12 Temple Road' END, @donor
            FROM booking.ServiceTypes WHERE Id = @typeId;
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """,
            new { no, donor, startsAt, location, typeId });
        return (id, no);
    }

    private async Task<int> BlocksOfAsync(int leaderId)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM booking.LeaderTimeBlocks WHERE LeaderId = @leaderId;", new { leaderId });
    }

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

    /// <summary>Runs the assignments at once behind an update lock on their appointments' rows.</summary>
    private async Task<AppointmentResult[]> RaceOnTheRowsAsync(params (long Id, string No, Guid Leader)[] attempts)
    {
        // A platform connection: without a session context, RLS hides every appointment from the
        // holder, its SELECT matches no row, and nothing is locked.
        await using var holder = await PlatformConnectionAsync();
        await using var hold = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "SELECT Id FROM booking.ServiceAppointments WITH (UPDLOCK, ROWLOCK) WHERE Id IN @ids;",
            new { ids = attempts.Select(a => a.Id).ToArray() },
            hold);

        var running = attempts.Select(a => Task.Run(() => AssignAsync(a.No, new NoLocks(), a.Leader))).ToArray();

        await using (var observer = new SqlConnection(database.ConnectionString))
        {
            await observer.OpenAsync();
            await WaitForWaitersAsync(observer, "booking.ServiceAppointments", attempts.Length);
        }

        await hold.CommitAsync();
        return await Task.WhenAll(running);
    }

    // ------------------------------------------------------------------ assigning

    [Fact]
    public async Task AssigningALeaderApprovesTheRequestAndBlocksTheirTime()
    {
        var type = await ServiceTypeAsync(durationMinutes: 180);
        var leader = await LeaderAsync();
        var appointment = await AppointmentAsync(type, TenOClock);

        var result = await AssignAsync(appointment.No, new NoLocks(), leader.PublicId);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(AppointmentStatus.Approved, result.Status);
        Assert.Equal(12, await BlocksOfAsync(leader.Id));

        await using var connection = await PlatformConnectionAsync();
        Assert.Equal(
            ["AppointmentApproved", "LeaderAssigned"],
            await connection.QueryAsync<string>(
                "SELECT EventType FROM core.OutboxMessages WHERE AggregateId = @no ORDER BY EventType;", new { no = appointment.No }));
        Assert.Equal("Pending", await connection.ExecuteScalarAsync<string>(
            "SELECT Response FROM booking.AppointmentLeaders WHERE AppointmentId = @id;", new { id = appointment.Id }));
    }

    [Fact]
    public async Task ALeaderAlreadyCommittedIsNamedAndNothingIsWritten()
    {
        var type = await ServiceTypeAsync(durationMinutes: 120);
        var leader = await LeaderAsync("Ven. Busy");
        var first = await AppointmentAsync(type, TenOClock);
        var second = await AppointmentAsync(type, TenOClock.AddHours(1));
        Assert.True((await AssignAsync(first.No, new NoLocks(), leader.PublicId)).Succeeded);

        var result = await AssignAsync(second.No, new NoLocks(), leader.PublicId);

        Assert.Equal(ServiceErrors.LeaderNotFree, result.Error);
        Assert.Equal(["Assign Test Ven. Busy"], result.Leaders);
        Assert.Equal(8, await BlocksOfAsync(leader.Id));

        await using var connection = await PlatformConnectionAsync();
        Assert.Equal("Requested", await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM booking.ServiceAppointments WHERE Id = @id;", new { id = second.Id }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.AppointmentLeaders WHERE AppointmentId = @id;", new { id = second.Id }));
    }

    [Fact]
    public async Task AnOverlapOfOneQuarterHourIsEnoughToBeNamed()
    {
        // 11:00-12:00 is taken; 10:15-11:15 shares only its last quarter-hour, 11:00.
        var type = await ServiceTypeAsync(durationMinutes: 60);
        var leader = await LeaderAsync("Ven. Edge");
        var taken = await AppointmentAsync(type, TenOClock.AddHours(1));
        var earlier = await AppointmentAsync(type, TenOClock.AddMinutes(15));
        Assert.True((await AssignAsync(taken.No, new NoLocks(), leader.PublicId)).Succeeded);

        var result = await AssignAsync(earlier.No, new NoLocks(), leader.PublicId);

        Assert.Equal(ServiceErrors.LeaderNotFree, result.Error);
        Assert.Equal(["Assign Test Ven. Edge"], result.Leaders);
    }

    [Fact]
    public async Task ALeaderBeingAssignedElsewhereAsksTheCallerToTryAgain()
    {
        var type = await ServiceTypeAsync();
        var leader = await LeaderAsync();
        var appointment = await AppointmentAsync(type, TenOClock);
        var redis = _redis.GetDatabase();
        Assert.True(await redis.StringSetAsync(RedisLeaderLocks.KeyFor(leader.Id), "held-by-the-test", TimeSpan.FromSeconds(30), When.NotExists));

        try
        {
            Assert.Equal(ServiceErrors.TryAgain, (await AssignAsync(appointment.No, RedisLocks(TimeSpan.FromMilliseconds(300)), leader.PublicId)).Error);
            Assert.Equal(0, await BlocksOfAsync(leader.Id));
        }
        finally
        {
            await redis.KeyDeleteAsync(RedisLeaderLocks.KeyFor(leader.Id));
        }
    }

    [Fact]
    public async Task ServicesThatOnlyTouchCanShareALeader()
    {
        var type = await ServiceTypeAsync(durationMinutes: 60);
        var leader = await LeaderAsync();
        var first = await AppointmentAsync(type, TenOClock);
        var next = await AppointmentAsync(type, TenOClock.AddHours(1));

        Assert.True((await AssignAsync(first.No, new NoLocks(), leader.PublicId)).Succeeded);
        Assert.True((await AssignAsync(next.No, new NoLocks(), leader.PublicId)).Succeeded);
        Assert.Equal(8, await BlocksOfAsync(leader.Id));
    }

    [Fact]
    public async Task AHomeVisitKeepsItsTravelTimeFree()
    {
        // At the donor's place 10:00-11:00 with 30 minutes either side, so 11:15 at the temple clashes.
        var type = await ServiceTypeAsync(durationMinutes: 60, bufferMinutes: 30);
        var leader = await LeaderAsync("Ven. Travelling");
        var home = await AppointmentAsync(type, TenOClock, ServiceLocation.AtDonorPlace);
        var temple = await AppointmentAsync(type, TenOClock.AddMinutes(75));

        Assert.True((await AssignAsync(home.No, new NoLocks(), leader.PublicId)).Succeeded);
        Assert.Equal(8, await BlocksOfAsync(leader.Id));
        Assert.Equal(ServiceErrors.LeaderNotFree, (await AssignAsync(temple.No, new NoLocks(), leader.PublicId)).Error);
    }

    [Fact]
    public async Task ALeaderOnLeaveCannotBeAssigned()
    {
        var type = await ServiceTypeAsync(durationMinutes: 60);
        var leader = await LeaderAsync("Ven. Away");
        var appointment = await AppointmentAsync(type, TenOClock);

        await using (var connection = await PlatformConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                DECLARE @off INT;
                INSERT org.LeaderTimeOff (LeaderId, InstitutionId, StartsAt, EndsAt, CreatedBy)
                VALUES (@leader, @institution, @from, DATEADD(HOUR, 2, @from), @by);
                SET @off = SCOPE_IDENTITY();
                INSERT booking.LeaderTimeBlocks (LeaderId, BlockStart, InstitutionId, Kind, TimeOffId)
                VALUES (@leader, DATEADD(MINUTE, 30, @from), @institution, 'TimeOff', @off);
                """,
                new { leader = leader.Id, institution = database.AlphaInstitutionId, from = TenOClock.AddMinutes(-30), by = SethikaTestDatabase.AlphaUserId });
        }

        Assert.Equal(ServiceErrors.LeaderNotFree, (await AssignAsync(appointment.No, new NoLocks(), leader.PublicId)).Error);
    }

    [Fact]
    public async Task OnlyActiveLeadersOfTheInstitutionCanBeAssigned()
    {
        var type = await ServiceTypeAsync();
        var appointment = await AppointmentAsync(type, TenOClock);
        var elsewhere = await LeaderAsync("Ven. Beta", database.BetaInstitutionId);
        var retired = await LeaderAsync("Ven. Retired", active: false);

        Assert.Equal(ServiceErrors.LeaderUnavailable, (await AssignAsync(appointment.No, new NoLocks(), elsewhere.PublicId)).Error);
        Assert.Equal(ServiceErrors.LeaderUnavailable, (await AssignAsync(appointment.No, new NoLocks(), retired.PublicId)).Error);
        Assert.Equal(0, await BlocksOfAsync(elsewhere.Id) + await BlocksOfAsync(retired.Id));
    }

    [Fact]
    public async Task AnotherInstitutionsAppointmentIsNotFoundEvenWhenTheCallerAskedForIt()
    {
        // Alpha's owner asked Beta for a service as a donor, so RLS's owner branch shows it to them.
        // Acting for Alpha, it is still not theirs to staff.
        var betaType = await ServiceTypeAsync(institutionId: database.BetaInstitutionId);
        var betaAppointment = await AppointmentAsync(betaType, TenOClock, donorId: SethikaTestDatabase.AlphaUserId);
        var leader = await LeaderAsync();

        Assert.Equal(AppointmentResult.NotFound, (await AssignAsync(betaAppointment.No, new NoLocks(), leader.PublicId)).Error);
        Assert.Equal(0, await BlocksOfAsync(leader.Id));
    }

    // ------------------------------------------------------------------ races

    [Fact]
    public async Task WithoutTheLock_TwoOverlappingAssignmentsOfOneLeaderAtOnce_OnlyOneHolds()
    {
        // Both read the leader as free; the primary key alone decides (V2).
        var type = await ServiceTypeAsync(durationMinutes: 120);
        var leader = await LeaderAsync("Ven. Raced");
        var first = await AppointmentAsync(type, TenOClock);
        var second = await AppointmentAsync(type, TenOClock.AddHours(1));

        var results = await RaceOnTheRowsAsync((first.Id, first.No, leader.PublicId), (second.Id, second.No, leader.PublicId));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == ServiceErrors.LeaderNotFree);
        Assert.Equal(8, await BlocksOfAsync(leader.Id));

        await using var connection = await PlatformConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.ServiceAppointments WHERE Id IN @ids AND Status = 'Approved';", new { ids = new[] { first.Id, second.Id } }));
    }

    [Fact]
    public async Task WithTheLock_TwoOverlappingAssignmentsOfOneLeader_TheSecondIsToldWhoIsBusy()
    {
        var type = await ServiceTypeAsync(durationMinutes: 120);
        var leader = await LeaderAsync("Ven. Locked");
        var first = await AppointmentAsync(type, TenOClock);
        var second = await AppointmentAsync(type, TenOClock.AddHours(1));

        var key = RedisLeaderLocks.KeyFor(leader.Id);
        var redis = _redis.GetDatabase();
        Assert.True(await redis.StringSetAsync(key, "held-by-the-test", TimeSpan.FromSeconds(30), When.NotExists));

        var running = new[] { first.No, second.No }.Select(no => Task.Run(() => AssignAsync(no, RedisLocks(TimeSpan.FromSeconds(20)), leader.PublicId))).ToArray();
        await Task.Delay(300);
        Assert.All(running, t => Assert.False(t.IsCompleted));
        await redis.KeyDeleteAsync(key);

        var results = await Task.WhenAll(running);

        Assert.Single(results, r => r.Succeeded);
        var refused = Assert.Single(results, r => !r.Succeeded);
        Assert.Equal(ServiceErrors.LeaderNotFree, refused.Error);
        Assert.Equal(["Assign Test Ven. Locked"], refused.Leaders);
        Assert.Equal(8, await BlocksOfAsync(leader.Id));
    }

    [Fact]
    public async Task TwoAdministratorsFillingTheLastPlaceAtOnce_OnlyOneWins()
    {
        // Two leaders needed, one assigned; two admins each add one more at the same moment. Both
        // count one live leader, so only the appointment's RowVersion can stop the second.
        var type = await ServiceTypeAsync(durationMinutes: 60, leadersRequired: 2);
        var first = await LeaderAsync("Ven. First");
        var second = await LeaderAsync("Ven. Second");
        var third = await LeaderAsync("Ven. Third");
        var appointment = await AppointmentAsync(type, TenOClock);
        Assert.True((await AssignAsync(appointment.No, new NoLocks(), first.PublicId)).Succeeded);

        var results = await RaceOnTheRowsAsync((appointment.Id, appointment.No, second.PublicId), (appointment.Id, appointment.No, third.PublicId));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => r.Error == AppointmentResult.Stale);

        await using var connection = await PlatformConnectionAsync();
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM booking.AppointmentLeaders WHERE AppointmentId = @id AND Response = 'Pending';", new { id = appointment.Id }));
    }

    // ------------------------------------------------------------------ the leader locks

    [Fact]
    public async Task LeaderLocksAreTakenLowestIdFirst()
    {
        // Leader 3 is held. Asked for 5 and 3, the lock must reach for 3 first and wait there,
        // never holding 5: that order is what keeps two administrators from deadlocking (V3).
        var low = await LeaderAsync("Ven. Low");
        var high = await LeaderAsync("Ven. High");
        var redis = _redis.GetDatabase();
        Assert.True(await redis.StringSetAsync(RedisLeaderLocks.KeyFor(low.Id), "held-by-the-test", TimeSpan.FromSeconds(30), When.NotExists));

        try
        {
            var attempt = RedisLocks(TimeSpan.FromSeconds(2)).AcquireAsync([high.Id, low.Id]);
            await Task.Delay(500);

            Assert.False(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(high.Id)));
            await using var lease = await attempt;
            Assert.Equal(LockOutcome.Busy, lease.Outcome);
        }
        finally
        {
            await redis.KeyDeleteAsync(RedisLeaderLocks.KeyFor(low.Id));
        }
    }

    [Fact]
    public async Task ABusyLeaderGivesBackTheLocksAlreadyTaken()
    {
        var low = await LeaderAsync("Ven. Low");
        var high = await LeaderAsync("Ven. High");
        var redis = _redis.GetDatabase();
        Assert.True(await redis.StringSetAsync(RedisLeaderLocks.KeyFor(high.Id), "held-by-the-test", TimeSpan.FromSeconds(30), When.NotExists));

        try
        {
            await using var lease = await RedisLocks(TimeSpan.FromMilliseconds(300)).AcquireAsync([low.Id, high.Id]);

            Assert.Equal(LockOutcome.Busy, lease.Outcome);
            Assert.False(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(low.Id)));
        }
        finally
        {
            await redis.KeyDeleteAsync(RedisLeaderLocks.KeyFor(high.Id));
        }
    }

    [Fact]
    public async Task AllLeadersLocksAreReleasedTogether()
    {
        var a = await LeaderAsync("Ven. A");
        var b = await LeaderAsync("Ven. B");
        var redis = _redis.GetDatabase();

        await using (var lease = await RedisLocks().AcquireAsync([a.Id, b.Id]))
        {
            Assert.Equal(LockOutcome.Acquired, lease.Outcome);
            Assert.True(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(a.Id)));
            Assert.True(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(b.Id)));
        }

        Assert.False(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(a.Id)));
        Assert.False(await redis.KeyExistsAsync(RedisLeaderLocks.KeyFor(b.Id)));
    }
}

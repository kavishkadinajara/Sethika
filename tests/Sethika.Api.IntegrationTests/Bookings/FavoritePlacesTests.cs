using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;
using Sethika.Application.Bookings.Places;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Api.IntegrationTests.Bookings;

/// <summary>
/// Plan 07, H12: the heart on an institution's page and "My places", against real SQL Server. A
/// person keeps a place once however often they tap, lets it go, and sees only active places - while
/// Row-Level Security shows each person's places to them alone.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class FavoritePlacesTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "favorite-places-test-signing-key-at-least-32-bytes";
    private const string Alpha = "alpha-temple";
    private const string Beta = "beta-temple";

    private readonly List<Guid> _users = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new LoopbackProxyStartupFilter()));
        });
    }

    public async Task DisposeAsync()
    {
        // The seed puts Beta back to Active and empties every place.
        await database.SeedAsync();
        await ExecuteAsync(
            """
            DELETE FROM auth.UserRoles WHERE UserId IN @users;
            DELETE FROM auth.Users WHERE Id IN @users;
            """,
            new { users = _users });

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class LoopbackProxyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private async Task<SqlConnection> PlatformConnectionAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1, @read_only = 1;");
        return connection;
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = await PlatformConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private SethikaDbContext ContextFor(ICurrentUserContext user) =>
        new(new DbContextOptionsBuilder<SethikaDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new SessionContextConnectionInterceptor(user))
            .Options);

    private SqlConnectionFactory ConnectionsFor(ICurrentUserContext user) => new(database.ConnectionString, user);

    /// <summary>A signed-up donor: a confirmed phone and the global DONOR role.</summary>
    private async Task<Guid> DonorAsync()
    {
        var id = Guid.CreateVersion7();
        var phone = "+9476" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _users.Add(id);

        await ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PreferredLanguage, Status, SecurityStamp)
            VALUES (@id, 'Donor', N'Heart Donor', @phone, 1, 'en', 'Active', 'stamp');
            INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
            SELECT @id, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = 'DONOR' AND r.InstitutionId IS NULL;
            """,
            new { id, phone });

        return id;
    }

    private HttpClient ClientFor(Guid? userId)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.8.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        if (userId is { } id)
        {
            var tokens = _host.Services.GetRequiredService<ITokenService>().Issue(new TokenSubject(id, null, IsPlatform: false));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access.Token);
        }

        return client;
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, HttpStatusCode expected)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} {text}");
            return text.Length == 0 ? default : JsonElement.Parse(text).Clone();
        }
    }

    private async Task<string[]> PlacesAsync(Guid user) =>
        [.. (await SendAsync(ClientFor(user), HttpMethod.Get, "/api/me/places", HttpStatusCode.OK))
            .EnumerateArray().Select(p => p.GetProperty("slug").GetString()!)];

    private async Task<int> RowsAsync(Guid user)
    {
        await using var connection = await PlatformConnectionAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM auth.FavoriteInstitutions WHERE UserId = @user;", new { user });
    }

    // ------------------------------------------------------------------ keeping and letting go

    [Fact]
    public async Task APlaceIsKeptOnceHoweverOftenItIsTapped_ListedNewestFirst_AndLetGoAlone()
    {
        var donor = await DonorAsync();

        await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        Assert.Equal([Alpha], await PlacesAsync(donor));
        Assert.Equal(1, await RowsAsync(donor));

        // datetime2(3): a few milliseconds apart, so "newest first" is not a tie.
        await Task.Delay(20);
        await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Beta}", HttpStatusCode.NoContent);
        var places = await SendAsync(ClientFor(donor), HttpMethod.Get, "/api/me/places", HttpStatusCode.OK);
        Assert.Equal([Beta, Alpha], places.EnumerateArray().Select(p => p.GetProperty("slug").GetString()));
        Assert.Equal("Beta Temple", places[0].GetProperty("name").GetString());

        // Letting one go leaves the other; letting it go again, or one never kept, is still 204.
        await SendAsync(ClientFor(donor), HttpMethod.Delete, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        Assert.Equal([Beta], await PlacesAsync(donor));
        await SendAsync(ClientFor(donor), HttpMethod.Delete, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        await SendAsync(ClientFor(donor), HttpMethod.Delete, "/api/me/places/no-such-place", HttpStatusCode.NoContent);
        Assert.Equal([Beta], await PlacesAsync(donor));
    }

    [Fact]
    public async Task OnlyAnActivePlaceCanBeKept_AndAKeptPlaceThatClosesIsNotListed_ButCanStillBeLetGo()
    {
        var donor = await DonorAsync();
        await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Beta}", HttpStatusCode.NoContent);

        var unknown = await SendAsync(ClientFor(donor), HttpMethod.Put, "/api/me/places/no-such-place", HttpStatusCode.NotFound);
        Assert.Equal(FavoritePlacesHandler.NotFound, unknown.GetProperty("code").GetString());
        await SendAsync(ClientFor(donor), HttpMethod.Put, "/api/me/places/" + new string('a', 151), HttpStatusCode.NotFound);

        await ExecuteAsync("UPDATE org.Institutions SET Status = 'Suspended' WHERE Slug = @slug;", new { slug = Beta });
        Assert.Empty(await PlacesAsync(donor));
        Assert.Equal(1, await RowsAsync(donor));
        var suspended = await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Beta}", HttpStatusCode.NotFound);
        Assert.Equal(FavoritePlacesHandler.NotFound, suspended.GetProperty("code").GetString());

        await SendAsync(ClientFor(donor), HttpMethod.Delete, $"/api/me/places/{Beta}", HttpStatusCode.NoContent);
        Assert.Equal(0, await RowsAsync(donor));
    }

    [Fact]
    public async Task EveryPlacesEndpointNeedsASignIn()
    {
        await SendAsync(ClientFor(null), HttpMethod.Get, "/api/me/places", HttpStatusCode.Unauthorized);
        await SendAsync(ClientFor(null), HttpMethod.Put, $"/api/me/places/{Alpha}", HttpStatusCode.Unauthorized);
        await SendAsync(ClientFor(null), HttpMethod.Delete, $"/api/me/places/{Alpha}", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoTapsAtOnce_KeepThePlaceOnce_AndBothAreAnswered204()
    {
        var donor = await DonorAsync();
        var alphaId = database.AlphaInstitutionId;

        // The other tap's insert, not yet committed: this request finds no row (it reads the last
        // committed version), inserts, and waits on the key until the other commits.
        await using var holder = await PlatformConnectionAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "INSERT auth.FavoriteInstitutions (UserId, InstitutionId) VALUES (@donor, @alphaId);",
            new { donor, alphaId },
            transaction);

        var tap = SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        await WaitForBlockedAsync();
        await transaction.CommitAsync();

        await tap;
        Assert.Equal(1, await RowsAsync(donor));
        Assert.Equal([Alpha], await PlacesAsync(donor));
    }

    /// <summary>Waits until a request in this database waits on a lock, so the race is really run.</summary>
    private async Task WaitForBlockedAsync()
    {
        await using var watcher = new SqlConnection(database.ConnectionString);
        await watcher.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await watcher.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id <> 0;") < 1)
        {
            Assert.True(DateTime.UtcNow < deadline, "The request never waited on the other tap's key.");
            await Task.Delay(50);
        }
    }

    // ------------------------------------------------------------------ Row-Level Security

    [Fact]
    public async Task APersonsPlacesAreTheirsAlone()
    {
        var donor = await DonorAsync();
        var stranger = await DonorAsync();
        await SendAsync(ClientFor(donor), HttpMethod.Put, $"/api/me/places/{Alpha}", HttpStatusCode.NoContent);
        await SendAsync(ClientFor(stranger), HttpMethod.Put, $"/api/me/places/{Beta}", HttpStatusCode.NoContent);

        async Task<int> SeenBy(ICurrentUserContext user)
        {
            await using var connection = await ConnectionsFor(user).OpenConnectionAsync();
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM auth.FavoriteInstitutions WHERE UserId = @donor;", new { donor });
        }

        Assert.Equal(1, await SeenBy(TestCurrentUserContext.Platform()));
        Assert.Equal(1, await SeenBy(TestCurrentUserContext.ForUser(donor)));
        Assert.Equal(0, await SeenBy(TestCurrentUserContext.ForUser(stranger)));
        Assert.Equal(0, await SeenBy(TestCurrentUserContext.ForInstitution(database.AlphaInstitutionId)));
        Assert.Equal(0, await SeenBy(TestCurrentUserContext.Anonymous()));

        // The reads and the letting go name the person as well as RLS: with the platform's reach,
        // which sees every row, each still touches only that person's.
        var platformQueries = new DonorBookingQueries(ConnectionsFor(TestCurrentUserContext.Platform()));
        Assert.Equal([Beta], (await platformQueries.ListPlacesAsync(stranger)).Select(p => p.Slug));
        await using (var context = ContextFor(TestCurrentUserContext.Platform()))
        {
            await new FavoritePlacesHandler(context, TimeProvider.System).RemoveAsync(stranger, Alpha);
        }

        Assert.Equal(1, await RowsAsync(donor));

        // A stranger cannot keep a place in the donor's name, nor move the donor's to themselves.
        await using (var connection = await ConnectionsFor(TestCurrentUserContext.ForUser(stranger)).OpenConnectionAsync())
        {
            var forged = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "INSERT auth.FavoriteInstitutions (UserId, InstitutionId) VALUES (@donor, @beta);",
                new { donor, beta = database.BetaInstitutionId }));
            Assert.Contains("block predicate", forged.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var connection = await ConnectionsFor(TestCurrentUserContext.ForUser(donor)).OpenConnectionAsync())
        {
            var moved = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "UPDATE auth.FavoriteInstitutions SET UserId = @stranger WHERE UserId = @donor;",
                new { stranger, donor }));
            Assert.Contains("block predicate", moved.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

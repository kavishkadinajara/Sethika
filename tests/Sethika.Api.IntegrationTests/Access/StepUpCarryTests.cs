using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Plan 09, M28: a step-up survives a refresh only when the refresh also presents the access token
/// holding it, and only with the time it had left. The refresh cookie alone - all a thief of the
/// httpOnly cookie has - carries nothing (A20). Against real SQL Server and Redis.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class StepUpCarryTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "step-up-carry-test-signing-key-at-least-32-bytes";
    private const string Password = "Dana!2026Sethika";

    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(database.RedisConnectionString);
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter()));
        });
    }

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                DECLARE @ids TABLE (Id UNIQUEIDENTIFIER);
                INSERT @ids SELECT Id FROM auth.Users WHERE PhoneNumber IN @phones;
                UPDATE auth.RefreshTokens SET ReplacedByTokenId = NULL WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.RefreshTokens WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.LoginHistory  WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.UserRoles     WHERE UserId IN (SELECT Id FROM @ids);
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        await _redis.DisposeAsync();
        await _host.DisposeAsync();
    }

    private sealed class LoopbackConnectionStartupFilter : IStartupFilter
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

    private sealed record Session(Guid UserId, string Refresh, string Csrf, string AccessToken)
    {
        public string TokenId => new JsonWebToken(AccessToken).Id;
    }

    private HttpClient NewClient()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");
        return client;
    }

    private async Task<Session> SignedInAsync(bool inAlpha = false)
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);
        var id = Guid.CreateVersion7();

        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
                VALUES (@id, 'InstitutionStaff', N'Carry Test', @phone, 1, @hash, 'en', 'Active');
                IF @inAlpha = 1
                    INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
                    SELECT @id, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.Code = 'ALPHA_STEWARD';
                """,
                new { id, phone, hash, inAlpha });
        }

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parse(id, response, await response.Content.ReadAsStringAsync());
    }

    private static Session Parse(Guid userId, HttpResponseMessage response, string body)
    {
        var cookies = SetCookieHeaderValue.ParseStrictList([.. response.Headers.GetValues("Set-Cookie")]);
        return new Session(
            userId,
            cookies.Single(c => c.Name == "sethika-refresh").Value.ToString(),
            cookies.Single(c => c.Name == "sethika-csrf").Value.ToString(),
            JsonElement.Parse(body).GetProperty("accessToken").GetString()!);
    }

    /// <summary>A rotation with the cookies a browser attaches and, if given, a bearer token beside them.</summary>
    private async Task<Session> RotateAsync(Session session, string path = "refresh", string? bearer = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/" + path);
        request.Headers.Add("Cookie", $"sethika-refresh={session.Refresh}; sethika-csrf={session.Csrf}");
        request.Headers.Add("X-CSRF-Token", session.Csrf);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return Parse(session.UserId, response, body);
    }

    private Task<bool> GrantAsync(Session session, TimeSpan lifetime) =>
        _redis.GetDatabase().StringSetAsync(RedisStepUpGrants.KeyFor(session.UserId, session.TokenId), 1, lifetime);

    private async Task<TimeSpan?> GrantLeftAsync(Guid userId, string tokenId) =>
        await _redis.GetDatabase().KeyTimeToLiveAsync(RedisStepUpGrants.KeyFor(userId, tokenId));

    [Fact]
    public async Task ARefreshThatShowsTheStepUppedToken_CarriesTheGrant_WithOnlyTheTimeItHadLeft()
    {
        var before = await SignedInAsync();
        await GrantAsync(before, TimeSpan.FromSeconds(90));

        var after = await RotateAsync(before, bearer: before.AccessToken);

        Assert.NotEqual(before.TokenId, after.TokenId);
        var left = await GrantLeftAsync(after.UserId, after.TokenId);
        Assert.NotNull(left);
        Assert.InRange(left.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(90));
    }

    [Fact]
    public async Task TheRefreshCookieAlone_CarriesNothing()
    {
        var before = await SignedInAsync();
        await GrantAsync(before, TimeSpan.FromMinutes(10));

        var after = await RotateAsync(before);

        Assert.Null(await GrantLeftAsync(after.UserId, after.TokenId));
    }

    [Fact]
    public async Task AnotherPersonsToken_CarriesNothingIntoThisSession()
    {
        var victim = await SignedInAsync();
        var other = await SignedInAsync();
        await GrantAsync(other, TimeSpan.FromMinutes(10));

        // The victim's cookie with someone else's stepped-up token beside it.
        var after = await RotateAsync(victim, bearer: other.AccessToken);

        Assert.Null(await GrantLeftAsync(victim.UserId, after.TokenId));
        Assert.Null(await GrantLeftAsync(other.UserId, after.TokenId));
    }

    [Fact]
    public async Task ATokenWithoutAGrant_GivesTheNextOneNone()
    {
        var before = await SignedInAsync();

        var after = await RotateAsync(before, bearer: before.AccessToken);

        Assert.Null(await GrantLeftAsync(after.UserId, after.TokenId));
    }

    [Fact]
    public async Task LeavingAnInstitutionForOnesOwnAccount_CarriesTheGrantToo()
    {
        var inside = await SignedInAsync(inAlpha: true);
        await GrantAsync(inside, TimeSpan.FromMinutes(5));

        var personal = await RotateAsync(inside, "switch-personal", bearer: inside.AccessToken);

        Assert.NotNull(await GrantLeftAsync(personal.UserId, personal.TokenId));
    }
}

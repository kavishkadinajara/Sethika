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
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Domain.Access;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// <c>PUT /api/me/language</c> (plan 5.6) over HTTP, against real SQL Server and Redis: the caller's
/// own <c>auth.Users.PreferredLanguage</c>, and nobody else's.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class MeLanguageEndpointsTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "me-language-endpoint-test-signing-key-at-least-32-bytes";
    private const string Password = "Dana!2026Sethika";
    private const string Language = "/api/me/language";

    private readonly List<string> _phones = [];
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackConnectionStartupFilter()));
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
                DELETE FROM auth.Users         WHERE Id     IN (SELECT Id FROM @ids);
                """,
                new { phones = _phones });
        }

        await _host.DisposeAsync();
    }

    // ------------------------------------------------------------------ plumbing

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

    private HttpClient NewClient(string? accessToken = null)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // A client address of its own, so the per-client login budget is never what a test meets.
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(256)}.{RandomNumberGenerator.GetInt32(1, 255)}");

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    /// <summary>A donor who prefers English, by phone number.</summary>
    private async Task<string> CreateDonorAsync()
    {
        var phone = "+9477" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
        _phones.Add(phone);
        var hash = _host.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(new User(), Password);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PhoneConfirmed, PasswordHash, PreferredLanguage, Status)
            VALUES (NEWID(), 'Donor', N'Language Test', @phone, 1, @hash, 'en', 'Active');
            """,
            new { phone, hash });

        return phone;
    }

    private async Task<string> LogInAsync(string phone)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> PutLanguageAsync(string? accessToken, string language)
    {
        using var client = NewClient(accessToken);
        return await client.PutAsJsonAsync(Language, new { language });
    }

    private async Task<string> PreferredLanguageOf(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
            "SELECT PreferredLanguage FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }) ?? "";
    }

    private async Task<byte[]> RowVersionOf(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<byte[]>(
            "SELECT RowVersion FROM auth.Users WHERE PhoneNumber = @phone;", new { phone }) ?? [];
    }

    /// <summary>Every user's id, language and row version, except the named one.</summary>
    private async Task<List<string>> EveryoneElse(string phone)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var rows = await connection.QueryAsync<string>(
            """
            SELECT CONVERT(VARCHAR(36), u.Id) + ' ' + u.PreferredLanguage + ' ' + CONVERT(VARCHAR(20), CONVERT(BIGINT, u.RowVersion))
            FROM auth.Users AS u
            WHERE u.PhoneNumber IS NULL OR u.PhoneNumber <> @phone
            ORDER BY u.Id;
            """,
            new { phone });

        return [.. rows];
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task SinhalaThenEnglishRoundTrips()
    {
        var phone = await CreateDonorAsync();
        var token = await LogInAsync(phone);

        using (var response = await PutLanguageAsync(token, "si"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal("si", await PreferredLanguageOf(phone));

        using (var response = await PutLanguageAsync(token, "en"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal("en", await PreferredLanguageOf(phone));
    }

    [Fact]
    public async Task SettingTheSameLanguageAgainIs204AndWritesNothing()
    {
        var phone = await CreateDonorAsync();
        var token = await LogInAsync(phone);

        using (var first = await PutLanguageAsync(token, "ta"))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        var version = await RowVersionOf(phone);

        using (var second = await PutLanguageAsync(token, "TA"))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        Assert.Equal("ta", await PreferredLanguageOf(phone));
        Assert.Equal(version, await RowVersionOf(phone));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData("sinhala")]
    public async Task AnUnsupportedLanguageIs400AndChangesNothing(string language)
    {
        var phone = await CreateDonorAsync();
        var token = await LogInAsync(phone);
        var version = await RowVersionOf(phone);

        using var response = await PutLanguageAsync(token, language);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_language", await CodeOf(response));
        Assert.Equal("en", await PreferredLanguageOf(phone));
        Assert.Equal(version, await RowVersionOf(phone));
    }

    [Fact]
    public async Task WithoutATokenItIs401()
    {
        using var response = await PutLanguageAsync(accessToken: null, "si");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OnlyTheCallersOwnRowChanges()
    {
        // The user is the token's sub. Any other row - another test user, the seeded Alpha and Beta
        // users - keeps its language and its row version.
        var caller = await CreateDonorAsync();
        await CreateDonorAsync();
        var token = await LogInAsync(caller);
        var before = await EveryoneElse(caller);

        using var response = await PutLanguageAsync(token, "si");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("si", await PreferredLanguageOf(caller));
        Assert.Equal(before, await EveryoneElse(caller));
    }
}

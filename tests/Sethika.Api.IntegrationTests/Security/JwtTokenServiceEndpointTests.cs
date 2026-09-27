using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Access;
using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Sends tokens from <see cref="JwtTokenService"/> to the real API pipeline, against the real
/// database and Redis.
/// </summary>
/// <remarks>
/// The issuing side and the validating side are configured in different places - the service
/// here, the bearer options in <c>Program.cs</c> - and each can be internally consistent while
/// disagreeing with the other on issuer, audience, key encoding or a claim name. The result is a
/// login that succeeds and every call after it failing with 401, or worse, a claim silently not
/// read and a session running without its tenant. Only a round trip through the host shows the
/// two sides agree.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class JwtTokenServiceEndpointTests(SethikaTestDatabase database) : IAsyncLifetime
{
    private const string SigningKey = "jwt-endpoint-test-signing-key-at-least-32-bytes!!";

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static JwtTokenService Service(TimeProvider? time = null, string signingKey = SigningKey) =>
        JwtTokenServiceTests.CreateService(signingKey, time ?? TimeProvider.System);

    private async Task<HttpResponseMessage> GetAsync(string path, IssuedTokens tokens)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.Access.Token);

        return await client.GetAsync(path);
    }

    [Fact]
    public async Task TheApiAcceptsAnIssuedTokenAndReadsBackTheSameSessionContext()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/me/permissions", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var me = body.RootElement;

        Assert.Equal(SethikaTestDatabase.AlphaUserId, me.GetProperty("userId").GetGuid());
        Assert.Equal(database.AlphaInstitutionId, me.GetProperty("institutionId").GetInt32());
        Assert.False(me.GetProperty("isPlatform").GetBoolean());

        // The permissions come back from the view under RLS, so the institution in the token
        // reached the database session context, not just the response.
        Assert.Contains(
            "BOOKINGS.CALENDAR.VIEW",
            me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task TheApiReadsThePlatformFlagAndNoInstitution()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, InstitutionId: null, IsPlatform: true));

        using var response = await GetAsync("/api/me/permissions", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(body.RootElement.GetProperty("isPlatform").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("institutionId").ValueKind);
    }

    [Fact]
    public async Task AnIssuedTokenPassesAPermissionGuardedEndpoint()
    {
        var tokens = Service().Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/bookings/calendar", tokens);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKeyIsRefused()
    {
        var tokens = Service(signingKey: "a-different-key-also-long-enough-to-be-valid!!").Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/bookings/calendar", tokens);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    // Fourteen minutes old is still inside its fifteen. Without this control, the sixteen-minute
    // case could be passing because the token is refused for some other reason entirely.
    [InlineData(14, HttpStatusCode.OK)]
    // Sixteen minutes old is a minute past expiry, well beyond the API's thirty-second clock skew.
    [InlineData(16, HttpStatusCode.Unauthorized)]
    public async Task AnAccessTokenIsAcceptedForFifteenMinutesAndNoLonger(
        int minutesAgo,
        HttpStatusCode expected)
    {
        var issuedAt = new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

        var tokens = Service(issuedAt).Issue(
            new TokenSubject(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId, IsPlatform: false));

        using var response = await GetAsync("/api/bookings/calendar", tokens);

        Assert.Equal(expected, response.StatusCode);
    }
}

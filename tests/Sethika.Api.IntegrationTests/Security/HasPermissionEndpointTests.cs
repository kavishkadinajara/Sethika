using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Api.Security;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// End-to-end proof that [HasPermission("CODE")] actually guards an endpoint over HTTP,
/// against the real API pipeline, the real view and real Redis.
/// </summary>
/// <remarks>
/// The handler is unit-tested separately; this exists because a correct handler that is
/// mis-wired into the pipeline - wrong policy provider, missing UseAuthorization, an
/// attribute that never reaches the endpoint - would pass every unit test and still leave
/// the endpoint wide open.
///
/// Tokens are minted here directly. There is no login endpoint yet (Phase A auth), so this
/// mints exactly what that endpoint will eventually issue.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class HasPermissionEndpointTests(SethikaTestDatabase database)
    : IAsyncLifetime
{
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long!!";

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await database.SeedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Point the host at the test database and our own signing key.
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

    private HttpClient ClientFor(Guid? userId = null, int? institutionId = null)
    {
        var client = _factory.CreateClient();

        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", MintToken(id, institutionId));
        }

        return client;
    }

    private static string MintToken(Guid userId, int? institutionId)
    {
        var claims = new List<Claim>
        {
            new(SethikaClaimTypes.UserId, userId.ToString()),
        };

        if (institutionId is { } institution)
        {
            // Invariant on purpose: HttpCurrentUserContext parses it with InvariantCulture,
            // so minting it under the ambient locale could break the round trip.
            claims.Add(new Claim(
                SethikaClaimTypes.InstitutionId,
                institution.ToString(CultureInfo.InvariantCulture)));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: SethikaJwt.Issuer,
            audience: SethikaJwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Rejects_an_unauthenticated_request()
    {
        var response = await ClientFor().GetAsync("/api/bookings/calendar");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Allows_a_user_who_holds_the_permission()
    {
        var client = ClientFor(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId);

        var response = await client.GetAsync("/api/bookings/calendar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Beta's steward has no roles, so the endpoint must refuse them - 403, not 401: they
    /// are authenticated, just not permitted.
    /// </summary>
    [Fact]
    public async Task Forbids_a_user_who_does_not_hold_the_permission()
    {
        var client = ClientFor(SethikaTestDatabase.BetaUserId, database.BetaInstitutionId);

        var response = await client.GetAsync("/api/bookings/calendar");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Alpha's steward holds the permission in Alpha, not in Beta. Presenting Beta as the
    /// institution must not carry the grant across.
    /// </summary>
    [Fact]
    public async Task Forbids_a_permission_held_only_in_another_institution()
    {
        var client = ClientFor(SethikaTestDatabase.AlphaUserId, database.BetaInstitutionId);

        var response = await client.GetAsync("/api/bookings/calendar");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Guards_each_endpoint_by_its_own_code()
    {
        var client = ClientFor(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId);

        // Alpha holds both VIEW and APPROVE
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bookings/calendar")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/api/bookings/BK-ALPHA/approve", content: null)).StatusCode);
    }

    [Fact]
    public async Task Reports_the_callers_own_permissions()
    {
        var client = ClientFor(SethikaTestDatabase.AlphaUserId, database.AlphaInstitutionId);

        var response = await client.GetAsync("/api/me/permissions");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("BOOKINGS.CALENDAR.VIEW", body, StringComparison.Ordinal);
        Assert.Contains("BOOKINGS.CALENDAR.APPROVE", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoint_stays_open()
    {
        var response = await ClientFor().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

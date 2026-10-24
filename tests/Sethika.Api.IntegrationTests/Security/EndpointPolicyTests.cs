using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Every API endpoint says who may call it, and the anonymous ones are exactly the ones meant to be.
/// </summary>
/// <remarks>
/// The host has no fallback authorization policy, so an endpoint mapped without
/// <c>[HasPermission]</c>, <c>RequireAuthorization</c> or <c>AllowAnonymous</c> is open to anyone, and
/// nothing else would notice. Read from the host's own route table, so a new endpoint is checked the
/// moment it is mapped.
/// </remarks>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class EndpointPolicyTests(SethikaTestDatabase database)
{
    /// <summary>The endpoints anyone may call. Adding one is a decision this list records.</summary>
    private static readonly string[] AnonymousEndpoints =
    [
        // Signing up and in, before there is a token (plan 01).
        "POST /api/auth/login",
        "POST /api/auth/otp/request",
        "POST /api/auth/otp/sign-in",
        "POST /api/auth/otp/verify",
        "POST /api/auth/password/reset",
        "POST /api/auth/signup",

        // The refresh-token cookie is the credential here, not a bearer token.
        "POST /api/auth/session/logout",
        "POST /api/auth/session/logout-all",
        "POST /api/auth/session/refresh",
        "POST /api/auth/session/switch-institution",

        // Public text, read before signing up.
        "GET /api/legal/current",

        // Finding an institution and its open days (plan 03, C10).
        "GET /api/institutions",
        "GET /api/institutions/{slug}",
        "GET /api/institutions/{slug}/availability",
    ];

    private WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", "endpoint-policy-test-signing-key-at-least-32-bytes");
        });

    private static IEnumerable<RouteEndpoint> ApiEndpoints(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

    private static string Describe(RouteEndpoint endpoint) =>
        string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
        + " " + endpoint.RoutePattern.RawText!.TrimEnd('/');

    [Fact]
    public async Task EveryApiEndpointSaysWhoMayCallIt()
    {
        await using var host = Host();

        var undeclared = ApiEndpoints(host)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(Describe)
            .ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public async Task OnlyTheListedEndpointsAreAnonymous()
    {
        await using var host = Host();

        var anonymous = ApiEndpoints(host)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal);

        Assert.Equal(AnonymousEndpoints.Order(StringComparer.Ordinal), anonymous);
    }
}

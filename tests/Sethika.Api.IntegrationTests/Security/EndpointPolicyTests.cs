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
        "POST /api/auth/session/switch-personal",

        // The mobile app's twins of the above (Phase F): the refresh token travels in the body,
        // since an app has no cookie jar, and is the credential the same way.
        "POST /api/auth/mobile/login",
        "POST /api/auth/mobile/otp/sign-in",
        "POST /api/auth/mobile/session/logout",
        "POST /api/auth/mobile/session/logout-all",
        "POST /api/auth/mobile/session/refresh",
        "POST /api/auth/mobile/session/switch-institution",
        "POST /api/auth/mobile/signup",

        // Public text, read before signing up.
        "GET /api/legal/current",

        // Finding an institution and its open days (plan 03, C10).
        "GET /api/institutions",
        "GET /api/institutions/{slug}",
        "GET /api/institutions/{slug}/availability",

        // Searching across institutions (plan 06, G8): no account needed to look for a place to give.
        "GET /api/search",
        "GET /api/search/month",
        "GET /api/search/lookups",

        // The needs board, anonymous and limited per client like search (plan 09, M12).
        "GET /api/needs",
        "GET /api/needs/lookups",

        // PayHere's server has no account here: its signature is the credential (plan 09, M9).
        "POST /api/payments/payhere/notify",

        // The live calendars' hub (plan 07, H9, H29): it hints at the same open days, so the same
        // anyone may hear them. SignalR maps the connection and its negotiation for every method.
        "* /api/hubs/availability",
        "* /api/hubs/availability/negotiate",

        // A co-sponsor's link (plan 07, H17): the link is the credential, and its summary shows only
        // what the donor chose to share. Joining it needs an account.
        "GET /api/share/{token}",
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

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sethika.Api.IntegrationTests.Persistence;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Every API response, answered or refused, tells a browser not to frame it, sniff it, load anything
/// for it or pass on where it came from (hardening 2026-11-08, finding W1).
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class ApiSecurityHeadersTests(SethikaTestDatabase database)
{
    private WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", database.RedisConnectionString);
            builder.UseSetting("Jwt:SigningKey", "api-security-headers-test-signing-key-32-bytes");
        });

    [Theory]
    // Answered: the health probe, and a public read that goes to the database.
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/api/legal/current", HttpStatusCode.OK)]
    // Refused before any endpoint runs: no token for a signed-in route.
    [InlineData("/api/me", HttpStatusCode.Unauthorized)]
    // No route at all.
    [InlineData("/api/no-such-route", HttpStatusCode.NotFound)]
    public async Task EveryResponseCarriesTheHeaders(string path, HttpStatusCode expected)
    {
        await using var host = Host();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Single(response, "Content-Security-Policy"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
    }

    private static string Single(HttpResponseMessage response, string name)
    {
        var values = response.Headers.TryGetValues(name, out var found) ? found
            : response.Content.Headers.TryGetValues(name, out var content) ? content
            : [];
        return Assert.Single(values);
    }
}

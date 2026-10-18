using Microsoft.Extensions.Logging.Abstractions;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// The step-up store when Redis is gone. The grant itself is exercised over HTTP in
/// StepUpEndpointsTests; this is the case HTTP tests would need a second Redis to reach.
/// </summary>
public sealed class RedisStepUpGrantsTests
{
    [Fact]
    public async Task AnUnreachableStoreGrantsNothingAndConfirmsNothing()
    {
        // Fail closed, unlike the rate limiter: an outage must not let a sensitive action through
        // unverified. Port 1 has nothing listening.
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.AsyncTimeout = 500;

        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var grants = new RedisStepUpGrants(unreachable, NullLogger<RedisStepUpGrants>.Instance);
        var userId = Guid.NewGuid();

        Assert.False(await grants.GrantAsync(userId, "jti", TimeSpan.FromMinutes(15)));
        Assert.False(await grants.IsGrantedAsync(userId, "jti"));
    }

    [Fact]
    public void TheKeyNamesTheUserAndTheToken()
    {
        var userId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal("stepup:0f8fad5b-d9cb-469f-a165-70867728950e:abc123", RedisStepUpGrants.KeyFor(userId, "abc123"));
    }
}

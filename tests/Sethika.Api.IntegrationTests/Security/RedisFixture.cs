using StackExchange.Redis;
using Xunit;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// One Redis connection shared by every test class that needs one.
/// </summary>
/// <remarks>
/// <see cref="ConnectionMultiplexer"/> is expensive to create - it handshakes, fetches server
/// configuration and discovers topology - and xunit constructs a test class once per test method.
/// Connecting in the class constructor cost about five seconds per test against the local Redis 3.2
/// build: fourteen tests whose own work totalled under 200 ms took seventy seconds. That is also
/// how the multiplexer is meant to be used in production, where it is a singleton.
///
/// Unlike <c>SethikaTestDatabase</c>, this fixture needs no parallelisation lock: tests key their
/// data by a unique phone number rather than sharing rows, so there is no reseed for another class
/// to invalidate.
/// </remarks>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>Override with SETHIKA_TEST_REDIS to point at another Redis.</summary>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("SETHIKA_TEST_REDIS") ?? "127.0.0.1:6379,defaultDatabase=3";

    /// <summary>The shared multiplexer.</summary>
    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = ConfigurationOptions.Parse(ConnectionString);
        options.AbortOnConnectFail = false;
        options.ClientName = "Sethika.Tests";

        Redis = await ConnectionMultiplexer.ConnectAsync(options);
    }

    public async Task DisposeAsync()
    {
        if (Redis is ConnectionMultiplexer multiplexer)
        {
            await multiplexer.DisposeAsync();
        }
    }
}

/// <summary>Test collection for classes that share <see cref="RedisFixture"/>.</summary>
/// <remarks>
/// Parallelisation is left on: these tests do not contend for shared state, so serializing them
/// would cost time without buying isolation.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "sethika-redis";
}

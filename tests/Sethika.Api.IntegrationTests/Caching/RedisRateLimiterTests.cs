using Microsoft.Extensions.Logging.Abstractions;
using Sethika.Api.IntegrationTests.Security;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Caching;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Caching;

/// <summary>
/// The limiter against real Redis: the counting, the expiry and the atomicity are all Redis
/// behaviour, which a fake would only imitate.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class RedisRateLimiterTests(RedisFixture redis)
{
    private readonly RedisRateLimiter _limiter = new(redis.Redis, NullLogger<RedisRateLimiter>.Instance);

    /// <summary>A rule of its own per test, so tests never share a counter.</summary>
    private static RateLimitRule Rule(int limit, TimeSpan window) =>
        new($"test-{Guid.NewGuid():N}", limit, window);

    [Fact]
    public async Task AllowsUpToTheLimitThenRefusesWithTheTimeLeftInTheWindow()
    {
        var rule = Rule(3, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);
        }

        var refused = await _limiter.AcquireAsync(rule, "subject");

        Assert.False(refused.IsAllowed);
        Assert.InRange(refused.RetryAfter, TimeSpan.FromSeconds(55), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RetryAfterIsTheTimeLeftNotTheWholeWindow()
    {
        // A client told to wait the whole window after most of it has passed waits for nothing; the
        // header must reflect when the count actually resets.
        var rule = Rule(1, TimeSpan.FromSeconds(3));

        Assert.True((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var refused = await _limiter.AcquireAsync(rule, "subject");

        Assert.False(refused.IsAllowed);
        Assert.InRange(refused.RetryAfter, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.6));
    }

    [Fact]
    public async Task CountsEachSubjectSeparately()
    {
        var rule = Rule(1, TimeSpan.FromMinutes(1));

        Assert.True((await _limiter.AcquireAsync(rule, "+94771234567")).IsAllowed);
        Assert.False((await _limiter.AcquireAsync(rule, "+94771234567")).IsAllowed);
        Assert.True((await _limiter.AcquireAsync(rule, "+94777654321")).IsAllowed);
    }

    [Fact]
    public async Task ConcurrentRequestsCannotExceedTheLimit()
    {
        // INCR is atomic on the server, so forty requests at once get exactly ten admissions -
        // not ten per request that happened to read the count before the others wrote it.
        var rule = Rule(10, TimeSpan.FromMinutes(1));

        var decisions = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => _limiter.AcquireAsync(rule, "burst")));

        Assert.Equal(10, decisions.Count(d => d.IsAllowed));
    }

    [Fact]
    public async Task TheCountStartsAgainWhenTheWindowEnds()
    {
        var rule = Rule(1, TimeSpan.FromMilliseconds(700));

        Assert.True((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);
        Assert.False((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);

        await Task.Delay(TimeSpan.FromMilliseconds(1000));

        Assert.True((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);
    }

    [Fact]
    public async Task ACounterLeftWithoutAnExpiryIsGivenOne()
    {
        // What INCR-then-EXPIRE leaves behind when the process dies between the two: a counter
        // that never resets, and a subject refused forever. Counting through it must repair it.
        var rule = Rule(100, TimeSpan.FromMinutes(1));
        var key = RateLimitKey.For(rule.Name, "subject");
        await redis.Redis.GetDatabase().StringSetAsync(key, 5);

        Assert.True((await _limiter.AcquireAsync(rule, "subject")).IsAllowed);

        var ttl = await redis.Redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(55), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheKeyNeverContainsTheSubject()
    {
        const string subject = "dana.donor@example.lk";

        var key = RateLimitKey.For("login", subject);

        Assert.DoesNotContain(subject, key, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RateLimitKey.For("login", subject), key);
        Assert.Equal("rl:login:".Length + 32, key.Length);
    }

    [Fact]
    public async Task AllowsTheRequestWhenRedisIsUnreachable()
    {
        // Fail open: a cache outage must not lock every user out. Port 1 has nothing listening.
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.AsyncTimeout = 500;

        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var limiter = new RedisRateLimiter(unreachable, NullLogger<RedisRateLimiter>.Instance);

        var decision = await limiter.AcquireAsync(Rule(1, TimeSpan.FromMinutes(1)), "subject");

        Assert.True(decision.IsAllowed);
    }
}

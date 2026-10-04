using Microsoft.Extensions.Logging.Abstractions;
using Sethika.Application.Access.PasswordReset;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// The reset-ticket store against real Redis: single use, expiry, and what is kept. The flow around
/// it is exercised over HTTP in PasswordResetEndpointsTests.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class RedisPasswordResetTicketsTests(RedisFixture redis)
{
    private readonly RedisPasswordResetTickets _tickets =
        new(redis.Redis, NullLogger<RedisPasswordResetTickets>.Instance);

    private static string NewTicket() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static PasswordResetTicket NewBinding() => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ATicketRedeemsOnceForTheAccountAndStampItWasIssuedFor()
    {
        var ticket = NewTicket();
        var binding = NewBinding();
        Assert.True(await _tickets.StoreAsync(ticket, binding, TimeSpan.FromMinutes(10)));

        var first = await _tickets.RedeemAsync(ticket);
        var second = await _tickets.RedeemAsync(ticket);

        Assert.Equal(binding, first.Ticket);
        Assert.Null(second.Ticket);
        Assert.False(second.IsUnavailable);
    }

    [Fact]
    public async Task FindingATicketDoesNotSpendIt()
    {
        // What lets a refused password cost a correction rather than a second SMS.
        var ticket = NewTicket();
        var binding = NewBinding();
        await _tickets.StoreAsync(ticket, binding, TimeSpan.FromMinutes(10));

        Assert.Equal(binding, (await _tickets.FindAsync(ticket)).Ticket);
        Assert.Equal(binding, (await _tickets.FindAsync(ticket)).Ticket);
        Assert.Equal(binding, (await _tickets.RedeemAsync(ticket)).Ticket);
        Assert.Null((await _tickets.FindAsync(ticket)).Ticket);
    }

    [Fact]
    public async Task ConcurrentRedemptionsOfOneTicketSucceedOnlyOnce()
    {
        var ticket = NewTicket();
        await _tickets.StoreAsync(ticket, NewBinding(), TimeSpan.FromMinutes(10));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _tickets.RedeemAsync(ticket)));

        Assert.Single(outcomes, o => o.Ticket is not null);
    }

    [Fact]
    public async Task AnUnknownOrBlankTicketIsInvalid()
    {
        Assert.Null((await _tickets.RedeemAsync(NewTicket())).Ticket);
        Assert.Null((await _tickets.FindAsync(string.Empty)).Ticket);
        Assert.Null((await _tickets.RedeemAsync(string.Empty)).Ticket);
    }

    [Fact]
    public async Task OnlyAHashOfTheTicketIsKept_AndItExpires()
    {
        var ticket = NewTicket();
        await _tickets.StoreAsync(ticket, NewBinding(), TimeSpan.FromMinutes(10));

        var key = RedisPasswordResetTickets.KeyFor(ticket);
        var ttl = await redis.Redis.GetDatabase().KeyTimeToLiveAsync(key);

        Assert.DoesNotContain(ticket, key, StringComparison.Ordinal);
        Assert.Equal("pwreset:".Length + 64, key.Length);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(9.5), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task AnUnreachableStoreIssuesNothingAndConfirmsNothing()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.AsyncTimeout = 500;

        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var tickets = new RedisPasswordResetTickets(unreachable, NullLogger<RedisPasswordResetTickets>.Instance);

        Assert.False(await tickets.StoreAsync(NewTicket(), NewBinding(), TimeSpan.FromMinutes(10)));
        Assert.True((await tickets.FindAsync(NewTicket())).IsUnavailable);
        Assert.True((await tickets.RedeemAsync(NewTicket())).IsUnavailable);
    }
}

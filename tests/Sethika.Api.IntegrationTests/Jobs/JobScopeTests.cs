using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Api.IntegrationTests.Persistence;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure;
using Sethika.Infrastructure.Jobs;

namespace Sethika.Api.IntegrationTests.Jobs;

/// <summary>
/// Plan 02, 5.7: who <see cref="ICurrentUserContext"/> is in a request's scope and in a job's, and
/// what each one's connections can see under Row-Level Security.
/// </summary>
[Collection(SethikaDatabaseCollection.Name)]
public sealed class JobScopeTests(SethikaTestDatabase database) : IAsyncLifetime
{
    /// <summary>Stands in for the request's principal: the Alpha applicant, no institution.</summary>
    private sealed class AlphaRequestUser : ICurrentUserContext
    {
        public Guid? UserId => SethikaTestDatabase.AlphaUserId;

        public int? InstitutionId => null;

        public bool IsPlatform => false;

        public string? TokenId => null;
    }

    public Task InitializeAsync() => database.SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSethikaPersistence(database.ConnectionString);
        services.AddSethikaCurrentUser<AlphaRequestUser>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    private static async Task<string[]> RequestsSeenFromAsync(IServiceProvider scope)
    {
        await using var connection = await scope.GetRequiredService<IDbConnectionFactory>().OpenConnectionAsync();
        return [.. await connection.QueryAsync<string>("SELECT RequestNo FROM org.RegistrationRequests ORDER BY RequestNo;")];
    }

    [Fact]
    public async Task ARequestsScopeIsTheRequestsUser_AndSeesOnlyTheirOwnApplication()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();

        Assert.IsType<AlphaRequestUser>(scope.ServiceProvider.GetRequiredService<ICurrentUserContext>());
        Assert.Equal(["REQ-ALPHA"], await RequestsSeenFromAsync(scope.ServiceProvider));
    }

    [Fact]
    public async Task AJobsScopeIsThePlatform_AndSeesEveryApplication()
    {
        await using var services = Services();
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();

        var user = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();
        Assert.Equal((true, (Guid?)null, (int?)null), (user.IsPlatform, user.UserId, user.InstitutionId));
        Assert.Equal(["REQ-ALPHA", "REQ-BETA"], await RequestsSeenFromAsync(scope.ServiceProvider));
    }

    [Fact]
    public async Task AJobsScopeDoesNotLeakIntoTheNextScope()
    {
        await using var services = Services();
        await using (services.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope())
        {
        }

        await using var next = services.CreateAsyncScope();
        Assert.False(next.ServiceProvider.GetRequiredService<ICurrentUserContext>().IsPlatform);
    }
}

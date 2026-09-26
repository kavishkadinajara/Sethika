using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Infrastructure;

/// <summary>Wires the persistence and permission foundation into the host's container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the write path (EF Core) and the read path (Dapper). Both are stamped
    /// with the tenant session context on every connection open, so neither can be used
    /// without Row-Level Security in force.
    /// </summary>
    /// <remarks>
    /// The caller must also register an <see cref="ICurrentUserContext"/> - the Api binds
    /// it to the authenticated principal, background jobs bind it to a platform context.
    /// </remarks>
    public static IServiceCollection AddSethikaPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddScoped<SessionContextConnectionInterceptor>();

        services.AddDbContext<SethikaDbContext>((serviceProvider, options) =>
            options.UseSqlServer(connectionString)
                   .AddInterceptors(serviceProvider.GetRequiredService<SessionContextConnectionInterceptor>()));

        services.AddScoped<IDbConnectionFactory>(serviceProvider =>
            new SqlConnectionFactory(
                connectionString,
                serviceProvider.GetRequiredService<ICurrentUserContext>()));

        return services;
    }

    /// <summary>
    /// Registers Redis and the permission service.
    /// </summary>
    /// <remarks>
    /// The multiplexer is a singleton because it is designed to be: it multiplexes every
    /// command over a small number of long-lived connections, and creating one per scope
    /// exhausts sockets under load.
    ///
    /// AbortOnConnectFail is off so the API still starts when Redis is briefly unavailable -
    /// permission reads fall back to SQL rather than the whole platform failing to boot.
    /// </remarks>
    public static IServiceCollection AddSethikaPermissions(
        this IServiceCollection services,
        string redisConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConnectionString);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            options.ClientName = "Sethika.Api";

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddScoped<IPermissionService, PermissionService>();

        return services;
    }
}

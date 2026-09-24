using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;

namespace Sethika.Infrastructure;

/// <summary>Wires the persistence foundation into the host's container.</summary>
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
}

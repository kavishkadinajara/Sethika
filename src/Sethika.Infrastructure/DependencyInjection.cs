using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Notifications;
using Sethika.Infrastructure.Persistence;
using Sethika.Infrastructure.Queries;
using Sethika.Infrastructure.Security;
using StackExchange.Redis;

namespace Sethika.Infrastructure;

/// <summary>Wires the persistence, permission and access foundation into the host's container.</summary>
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

        // The same scoped instance under its Application-facing name, so a handler and anything
        // else in the request share one context, one connection and one transaction.
        services.AddScoped<ISethikaDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<SethikaDbContext>());

        services.AddScoped<IDbConnectionFactory>(serviceProvider =>
            new SqlConnectionFactory(
                connectionString,
                serviceProvider.GetRequiredService<ICurrentUserContext>()));

        services.AddScoped<ILegalDocumentQueries, LegalDocumentQueries>();

        return services;
    }

    /// <summary>
    /// Registers Redis, the permission service and the rate limiter.
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

        // Same Redis, same singleton multiplexer. Stateless, so one instance serves every request.
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();

        return services;
    }

    /// <summary>
    /// Registers what sign-up and sign-in are built from: one-time codes and the SMS sender that
    /// delivers them, password policy and hashing, phone normalisation, and token issuing.
    /// </summary>
    /// <remarks>
    /// Configuration is read here, at registration, rather than when a service is first resolved.
    /// The checks below are only worth having if they stop the host from starting; resolved lazily,
    /// a misconfigured production host would boot, report healthy, and fail on the first signup.
    ///
    /// Requires <see cref="AddSethikaPersistence"/> and <see cref="AddSethikaPermissions"/>: every
    /// SMS is recorded through the connection factory, and one-time codes live in Redis.
    /// </remarks>
    /// <param name="services">The host's container.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="isDevelopment">
    /// Whether the host runs in Development. Two settings write live one-time codes to the log, and
    /// both are refused anywhere else.
    /// </param>
    public static IServiceCollection AddSethikaAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var smsSection = configuration.GetSection(SmsOptions.SectionName);
        var otpSection = configuration.GetSection(OtpOptions.SectionName);

        services.Configure<SmsOptions>(smsSection);
        services.Configure<OtpOptions>(otpSection);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        if (!isDevelopment && (otpSection.Get<OtpOptions>()?.LogCodesForDevelopment ?? false))
        {
            throw new InvalidOperationException(
                "Otp:LogCodesForDevelopment is enabled outside Development. It writes live one-time " +
                "codes to the log, which makes the log a way to sign in as anyone; turn it off.");
        }

        AddSmsSender(services, smsSection.Get<SmsOptions>() ?? new SmsOptions(), isDevelopment);

        services.Configure<IdentityOptions>(options =>
        {
            // Identity's default lockout is five minutes. Five guesses per five minutes is 1 440 a
            // day against one account; per fifteen it is 480, and a legitimate user who mistypes
            // five times waits a quarter of an hour rather than being locked out for longer.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        });

        services.Configure<PasswordHasherOptions>(options =>
        {
            // Identity's default is 100 000 iterations of PBKDF2-HMAC-SHA512. OWASP's current
            // guidance for that PRF is 210 000, and the cost lands only on sign-in and password
            // change, where a few extra milliseconds are invisible. Hashes made at the old count
            // still verify, because the count is stored inside each hash.
            options.IterationCount = 210_000;
        });

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IPasswordPolicy, SethikaPasswordPolicy>();
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();

        // Singleton because it holds the signing credentials, and building them once means a bad
        // key fails on first use rather than differently on every request.
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<IOtpService, OtpService>();

        return services;
    }

    /// <summary>Registers the sender named by <c>Sms:Provider</c>, or refuses to start.</summary>
    private static void AddSmsSender(IServiceCollection services, SmsOptions sms, bool isDevelopment)
    {
        if (string.Equals(sms.Provider, SmsOptions.ConsoleProvider, StringComparison.OrdinalIgnoreCase))
        {
            // A console sender in production means no user ever receives a code and sign-up looks
            // simply broken, with nothing in the logs to say why - and it logs every code it
            // "sends". Refusing to start is the only outcome that gets noticed before users do.
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    "Sms:Provider is Console outside Development: no SMS would be delivered and every " +
                    "one-time code would be written to the log. Set Sms:Provider to NotifyLk.");
            }

            services.AddScoped<ISmsSender, ConsoleSmsSender>();
            return;
        }

        if (string.Equals(sms.Provider, SmsOptions.NotifyLkProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Through IHttpClientFactory, not a client constructed per call: a new HttpClient per
            // message leaves its socket in TIME_WAIT and exhausts ports under a burst of sends,
            // while one long-lived client never notices a DNS change. The factory pools handlers
            // and recycles them, which avoids both.
            services.AddHttpClient<ISmsSender, NotifyLkSmsSender>(client =>
                client.BaseAddress = new Uri(sms.NotifyLkBaseUrl));
            return;
        }

        throw new InvalidOperationException(
            $"Sms:Provider '{sms.Provider}' is not recognised. Use {SmsOptions.ConsoleProvider} " +
            $"(Development only) or {SmsOptions.NotifyLkProvider}.");
    }
}

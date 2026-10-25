using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Infrastructure.Caching;
using Sethika.Infrastructure.Files;
using Sethika.Infrastructure.Jobs;
using Sethika.Infrastructure.Messaging;
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

        // On the scoped context, so each runs inside the handler's own transaction.
        services.AddScoped<INumberSeries, SqlNumberSeries>();
        services.AddScoped<IRegistrationLocks, SqlRegistrationLocks>();
        services.AddScoped<IInstitutionModules, SqlInstitutionModules>();
        services.AddScoped<ISlotInventory, SqlSlotInventory>();

        services.AddScoped<IDbConnectionFactory>(serviceProvider =>
            new SqlConnectionFactory(
                connectionString,
                serviceProvider.GetRequiredService<ICurrentUserContext>()));

        // Dapper's handlers are process-wide; reads that return a date need this one.
        DateOnlyTypeHandler.Register();

        services.AddScoped<IAccountQueries, AccountQueries>();
        services.AddScoped<ILegalDocumentQueries, LegalDocumentQueries>();
        services.AddScoped<IRoleLevelQueries, RoleLevelQueries>();
        services.AddScoped<IOnboardingApplicationQueries, OnboardingApplicationQueries>();
        services.AddScoped<IRegistrationReviewQueries, RegistrationReviewQueries>();
        services.AddScoped<IInstitutionBookingQueries, InstitutionBookingQueries>();
        services.AddScoped<IDonorBookingQueries, DonorBookingQueries>();
        services.AddScoped<IBookingNotificationQueries, BookingNotificationQueries>();

        return services;
    }

    /// <summary>
    /// Registers Redis, the permission service, the rate limiter and the slot-day lock.
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

        // Read by the permission handler on every RequiresMfa check, so it lives with it here.
        services.AddSingleton<IStepUpGrants, RedisStepUpGrants>();

        // The booking engine's slot-day lock (plan 03, C2). Timings from SlotLocks, defaults otherwise.
        services.AddOptions<SlotLockOptions>().BindConfiguration(SlotLockOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISlotLocks, RedisSlotLocks>();

        // Leader assignment's lock (Phase E, V3), on the same store. Timings from LeaderLocks.
        services.AddOptions<LeaderLockOptions>().BindConfiguration(LeaderLockOptions.SectionName);
        services.AddSingleton<ILeaderLocks, RedisLeaderLocks>();

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

        // Stateless over the singleton multiplexer, like the step-up grants.
        services.AddSingleton<IPasswordResetTickets, RedisPasswordResetTickets>();

        return services;
    }

    /// <summary>Registers where uploaded files are kept (plan 02, B4).</summary>
    /// <remarks>
    /// Read at registration for the same reason as <see cref="AddSethikaAccess"/>: a host that
    /// would lose every upload must not start. Development defaults to a folder under the user's
    /// local application data; anywhere else <c>Files:LocalRoot</c> must be set.
    /// </remarks>
    public static IServiceCollection AddSethikaFiles(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var root = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>()?.LocalRoot;

        if (string.IsNullOrWhiteSpace(root))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    "Files:LocalRoot is not set. Uploaded documents would have nowhere to go; set it to an " +
                    "absolute directory outside the application's content root.");
            }

            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sethika", "files");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFileStorage>(serviceProvider =>
            new LocalFileStorage(root, serviceProvider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Binds <see cref="ICurrentUserContext"/>: <typeparamref name="TRequestUser"/> in a request's
    /// scope, <see cref="PlatformJobUser"/> in a scope from <see cref="JobScopes.CreatePlatformScope"/>.
    /// </summary>
    /// <remarks>
    /// One registration for both, so the persistence layer stamps every connection from whichever
    /// is in force without knowing there are two. The request's is the default: a scope is a job's
    /// only when job code says so.
    /// </remarks>
    public static IServiceCollection AddSethikaCurrentUser<TRequestUser>(this IServiceCollection services)
        where TRequestUser : class, ICurrentUserContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<JobScope>();
        services.AddScoped<TRequestUser>();
        services.AddScoped<ICurrentUserContext>(serviceProvider =>
            serviceProvider.GetRequiredService<JobScope>().IsJob
                ? PlatformJobUser.Instance
                : serviceProvider.GetRequiredService<TRequestUser>());

        return services;
    }

    /// <summary>
    /// Registers the outbox processor, and - unless <c>Jobs:Enabled</c> is false - Hangfire with
    /// SQL Server storage and the recurring jobs (plan 02, B10).
    /// </summary>
    /// <remarks>
    /// Hangfire keeps its tables in its own <c>HangFire</c> schema in the same database and creates
    /// them on first start. The SQL project does not own them, and its publish profile does not drop
    /// objects it does not know, so a publish leaves them alone. Hangfire's connections are not
    /// stamped with a session context: its tables are not under Row-Level Security, and its
    /// connections run no Sethika queries.
    /// </remarks>
    public static IServiceCollection AddSethikaJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<IUserTextMessages, UserTextMessages>();
        services.AddTransient<OutboxJob>();
        services.AddTransient<BookingReminderJob>();

        var jobs = configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();
        if (!jobs.Enabled)
        {
            return services;
        }

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions()));

        services.AddHangfireServer(options => options.WorkerCount = jobs.WorkerCount);
        services.AddHostedService<RecurringJobs>();

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
                    "one-time code would be written to the log. Set Sms:Provider to TextLk.");
            }

            services.AddScoped<ISmsSender, ConsoleSmsSender>();
            return;
        }

        if (string.Equals(sms.Provider, SmsOptions.TextLkProvider, StringComparison.OrdinalIgnoreCase))
        {
            // A typed factory client, for the same reasons as Notify.lk's below.
            services.AddHttpClient<ISmsSender, TextLkSmsSender>(client =>
                client.BaseAddress = new Uri(sms.TextLkBaseUrl));
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
            $"(Development only), {SmsOptions.TextLkProvider} or {SmsOptions.NotifyLkProvider}.");
    }
}

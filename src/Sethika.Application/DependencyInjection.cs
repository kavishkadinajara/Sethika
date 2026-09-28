using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Access.Bootstrap;
using Sethika.Application.Access.Session;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using Sethika.Application.Access.StepUp;

namespace Sethika.Application;

/// <summary>Registers the use-case handlers. Infrastructure registers what they depend on.</summary>
public static class DependencyInjection
{
    /// <summary>Registers every command handler, each scoped to the request that runs it.</summary>
    public static IServiceCollection AddSethikaApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singleton: hashed once per process, so an unknown login pays for one verification and
        // not for a hash as well - which would make it slower than a wrong password instead.
        services.AddSingleton<DummyPasswordHash>();

        services.AddScoped<SignInHandler>();
        services.AddScoped<SignUpDonorHandler>();
        services.AddScoped<SessionHandler>();
        services.AddScoped<StepUpHandler>();
        services.AddScoped<BootstrapPlatformAdminHandler>();

        return services;
    }
}

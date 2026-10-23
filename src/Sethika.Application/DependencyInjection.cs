using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Bootstrap;
using Sethika.Application.Access.Language;
using Sethika.Application.Access.PasswordChange;
using Sethika.Application.Access.PasswordReset;
using Sethika.Application.Access.Roles;
using Sethika.Application.Access.Session;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using Sethika.Application.Access.StepUp;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Slots;
using Sethika.Application.Onboarding.Applying;
using Sethika.Application.Onboarding.Approval;
using Sethika.Application.Onboarding.Notifications;
using Sethika.Application.Onboarding.Review;

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

        services.AddScoped<PasswordVerifier>();
        services.AddScoped<SessionStarter>();
        services.AddScoped<SignInHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<OtpSignInHandler>();
        services.AddScoped<SignUpDonorHandler>();
        services.AddScoped<SessionHandler>();
        services.AddScoped<StepUpHandler>();
        services.AddScoped<PasswordResetHandler>();
        services.AddScoped<BootstrapPlatformAdminHandler>();
        services.AddScoped<RoleManagementHandler>();
        services.AddScoped<PreferredLanguageHandler>();
        services.AddScoped<ApplicationHandler>();
        services.AddScoped<ApproveRegistrationHandler>();
        services.AddScoped<RegistrationReviewHandler>();
        services.AddScoped<BookMealHandler>();
        services.AddScoped<BookingDecisionHandler>();
        services.AddScoped<SlotManagementHandler>();

        // What the outbox processor dispatches to, by event type.
        services.AddScoped<IOutboxHandler, OnboardingSmsHandler>();

        return services;
    }
}

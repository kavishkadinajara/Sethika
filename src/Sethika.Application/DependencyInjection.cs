using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;
using Sethika.Application.Access.Bootstrap;
using Sethika.Application.Access.Language;
using Sethika.Application.Access.PasswordChange;
using Sethika.Application.Access.PasswordReset;
using Sethika.Application.Access.Platform;
using Sethika.Application.Access.Roles;
using Sethika.Application.Access.Session;
using Sethika.Application.Access.SignIn;
using Sethika.Application.Access.SignUp;
using Sethika.Application.Access.StepUp;
using Sethika.Application.Bookings.Discovery;
using Sethika.Application.Bookings.Engine;
using Sethika.Application.Bookings.Live;
using Sethika.Application.Bookings.Memorial;
using Sethika.Application.Bookings.Places;
using Sethika.Application.Bookings.Notifications;
using Sethika.Application.Bookings.Recurring;
using Sethika.Application.Bookings.Sharing;
using Sethika.Application.Bookings.Slots;
using Sethika.Application.Bookings.Sponsors;
using Sethika.Application.Bookings.Today;
using Sethika.Application.Giving.Donations;
using Sethika.Application.Giving.Needs;
using Sethika.Application.Giving.Payments;
using Sethika.Application.Onboarding.Applying;
using Sethika.Application.Onboarding.Approval;
using Sethika.Application.Onboarding.Notifications;
using Sethika.Application.Onboarding.Review;
using Sethika.Application.Services.Appointments;
using Sethika.Application.Services.Setup;

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
        services.AddScoped<PlatformAdminHandler>();
        services.AddScoped<TemplateSyncHandler>();
        services.AddScoped<PreferredLanguageHandler>();
        services.AddScoped<ApplicationHandler>();
        services.AddScoped<ApproveRegistrationHandler>();
        services.AddScoped<RegistrationReviewHandler>();
        services.AddScoped<BookMealHandler>();
        services.AddScoped<BookingDecisionHandler>();
        services.AddScoped<SlotManagementHandler>();
        services.AddScoped<StandingBlocks>();
        services.AddScoped<AvailabilityCalendar>();
        services.AddScoped<AvailabilitySearch>();
        services.AddScoped<TodaysTable>();
        services.AddScoped<BookingReminders>();
        services.AddScoped<RecurringBookingRun>();
        services.AddScoped<RecurringBookingHandler>();
        services.AddScoped<FavoritePlacesHandler>();
        services.AddScoped<MemorialPlanHandler>();
        services.AddScoped<MemorialPlanner>();
        services.AddScoped<MemorialReminders>();
        services.AddScoped<ContributorHandler>();
        services.AddScoped<ShareHandler>();
        services.AddScoped<DonorBookHandler>();
        services.AddScoped<DonationHandler>();
        services.AddScoped<PaymentMerchantHandler>();
        services.AddScoped<OnlinePaymentHandler>();
        services.AddScoped<NeedHandler>();
        services.AddScoped<PledgeHandler>();
        services.AddScoped<StandingDayBooker>();
        services.AddScoped<StandingSponsorshipHandler>();
        services.AddScoped<StandingSponsorshipPreview>();
        services.AddScoped<StandingSponsorshipReader>();
        services.AddScoped<StandingBookingRun>();
        services.AddScoped<DayRequestHandler>();
        services.AddScoped<StandingSwapHandler>();
        services.AddScoped<AssignLeadersHandler>();
        services.AddScoped<ServiceSetupHandler>();
        services.AddScoped<RequestServiceHandler>();
        services.AddScoped<AppointmentDecisionHandler>();
        services.AddScoped<AppointmentTexts>();
        services.AddScoped<AppointmentReminders>();

        // What the outbox processor dispatches to, by event type.
        services.AddScoped<IOutboxHandler, OnboardingSmsHandler>();
        services.AddScoped<IOutboxHandler, BookingSmsHandler>();
        services.AddScoped<IOutboxHandler, RecurringBookingSmsHandler>();
        services.AddScoped<IOutboxHandler, StandingSmsHandler>();
        services.AddScoped<IOutboxHandler, DonationSmsHandler>();
        services.AddScoped<IOutboxHandler, AvailabilityChangedHandler>();
        services.AddScoped<IOutboxHandler, AppointmentCancelledHandler>();
        services.AddScoped<IOutboxHandler, AppointmentSmsHandler>();
        services.AddScoped<IOutboxHandler, InstitutionGeoHandler>();

        return services;
    }
}

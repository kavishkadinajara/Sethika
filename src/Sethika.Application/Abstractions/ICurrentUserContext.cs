namespace Sethika.Application.Abstractions;

/// <summary>
/// Who the current request is acting as. Every database connection is stamped with
/// these three values through sp_set_session_context, which is what the Row-Level
/// Security predicates in the sec schema read.
/// </summary>
/// <remarks>
/// Cross-cutting on purpose: Booking, Giving and Onboarding all need it, so it is
/// not owned by any one module folder.
/// </remarks>
public interface ICurrentUserContext
{
    /// <summary>Signed-in user, or null for anonymous/public traffic.</summary>
    Guid? UserId { get; }

    /// <summary>Institution the user is acting inside, or null for donors and platform staff.</summary>
    int? InstitutionId { get; }

    /// <summary>
    /// True for platform staff and background jobs, which see across all tenants.
    /// Never derive this from client input.
    /// </summary>
    bool IsPlatform { get; }
}

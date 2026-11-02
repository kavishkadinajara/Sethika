using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sethika.Domain.Access;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;
using Sethika.Domain.Onboarding;
using Sethika.Domain.Services;

namespace Sethika.Application.Abstractions;

/// <summary>
/// The write path command handlers use: EF Core change tracking, set-based updates and
/// transactions over the tables commands write.
/// </summary>
/// <remarks>
/// Application depends on EF Core's core package here, not on SQL Server - see plan A16. The
/// implementation's connections are stamped with the RLS session context on open, so every
/// handler runs under Row-Level Security without doing anything to get it.
///
/// Reads for lists, search and dashboards do not come through here; they are Dapper queries.
/// </remarks>
public interface ISethikaDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Institution> Institutions { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }
    DbSet<PreviousPassword> PreviousPasswords { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<DonorProfile> DonorProfiles { get; }

    // A donor's preferred places (plan 07, H12): theirs alone, by RLS.
    DbSet<FavoriteInstitution> FavoriteInstitutions { get; }

    DbSet<UserConsent> UserConsents { get; }
    DbSet<LegalDocument> LegalDocuments { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    // Onboarding (plan 02). The application is one aggregate: its documents, office bearers,
    // checklist, questions and history are reached through it, never through sets of their own.
    DbSet<RegistrationRequest> RegistrationRequests { get; }
    DbSet<StoredFile> StoredFiles { get; }
    DbSet<DocumentType> DocumentTypes { get; }
    DbSet<InstitutionTypeRequiredDocument> InstitutionTypeRequiredDocuments { get; }
    DbSet<VerificationChecklistItem> VerificationChecklistItems { get; }

    // What approving an application creates (plan 02, B6).
    DbSet<InstitutionMember> InstitutionMembers { get; }
    DbSet<StaffInvitation> StaffInvitations { get; }
    DbSet<InstitutionDocument> InstitutionDocuments { get; }
    DbSet<InstitutionStatusChange> InstitutionStatusHistory { get; }
    DbSet<AuditLogEntry> AuditLogs { get; }

    // The RBAC catalogue, level ceilings and role grants, for checks that must read inside the
    // command's transaction rather than trust the permission cache.
    DbSet<Permission> Permissions { get; }
    DbSet<RoleLevel> RoleLevels { get; }
    DbSet<RoleLevelPermission> RoleLevelPermissions { get; }
    DbSet<RolePermission> RolePermissions { get; }

    /// <summary><c>auth.vw_EffectivePermissions</c>, read-only.</summary>
    DbSet<EffectivePermission> EffectivePermissions { get; }

    /// <summary><c>auth.vw_InstitutionPermissions</c>, read-only.</summary>
    DbSet<InstitutionPermission> InstitutionPermissions { get; }

    // Meal slots and bookings (plan 03). A slot's seasons and a booking's history are reached
    // through their aggregate. Slot instances have a set of their own: the engine reads and creates
    // them, but changes their counters only with a conditional UPDATE (C2).
    DbSet<MealSlot> MealSlots { get; }
    DbSet<BlockedDate> BlockedDates { get; }
    DbSet<SlotInstance> SlotInstances { get; }
    DbSet<Booking> Bookings { get; }

    // Yearly bookings (plan 07, H2). The year is claimed with a conditional UPDATE in the booking's
    // transaction, so two runs of the yearly job cannot both try it.
    DbSet<RecurringBooking> RecurringBookings { get; }

    // Co-sponsors (plan 07, H16-H19): the donor's invitations, in the donor's session. Joining and
    // declining go through IShareLinks, since the person answering is on neither row yet (H18).
    DbSet<BookingContributor> BookingContributors { get; }

    // The Memorial Dana Planner (plan 07, H6). Plans are the donor's alone (RLS, H13); rules are
    // reference data the application only reads.
    DbSet<MemorialPlan> MemorialPlans { get; }
    DbSet<MemorialRule> MemorialRules { get; }

    // An institution's donor book (plan 08, K1): its institution's alone, by RLS.
    DbSet<InstitutionDonor> InstitutionDonors { get; }

    // Religious services (Phase E). An appointment's leaders and history are reached through it.
    // Time blocks have a set of their own: handlers write and delete them beside the assignment or
    // leave they belong to, and their primary key is the overlap guarantee (V2).
    DbSet<ServiceType> ServiceTypes { get; }
    DbSet<ReligiousLeader> ReligiousLeaders { get; }
    DbSet<LeaderWeeklyHours> LeaderWeeklyHours { get; }
    DbSet<LeaderTimeOff> LeaderTimeOff { get; }
    DbSet<LeaderTimeBlock> LeaderTimeBlocks { get; }
    DbSet<ServiceAppointment> ServiceAppointments { get; }

    /// <summary>For transactions that span a set-based update and tracked inserts.</summary>
    DatabaseFacade Database { get; }

    /// <summary>To discard tracked entities after a failed save, before looking again.</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>For loading an aggregate's collections one query each (see <c>RegistrationLoading</c>).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

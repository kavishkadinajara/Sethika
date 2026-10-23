using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sethika.Domain.Access;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;
using Sethika.Domain.Onboarding;

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

    /// <summary>For transactions that span a set-based update and tracked inserts.</summary>
    DatabaseFacade Database { get; }

    /// <summary>To discard tracked entities after a failed save, before looking again.</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>For loading an aggregate's collections one query each (see <c>RegistrationLoading</c>).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

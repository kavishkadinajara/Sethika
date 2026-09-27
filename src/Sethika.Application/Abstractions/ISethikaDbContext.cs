using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sethika.Domain.Access;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;

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
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<DonorProfile> DonorProfiles { get; }
    DbSet<UserConsent> UserConsents { get; }
    DbSet<LegalDocument> LegalDocuments { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>For transactions that span a set-based update and tracked inserts.</summary>
    DatabaseFacade Database { get; }

    /// <summary>To discard tracked entities after a failed save, before looking again.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence;

/// <summary>
/// EF Core is the write path only: change tracking, RowVersion concurrency and
/// transactions. Reads go through Dapper.
/// </summary>
/// <remarks>
/// There are no EF migrations and there never will be - src/Sethika.Database is the
/// schema source of truth and deploys as a dacpac. This context only maps onto tables
/// that already exist, so nothing here should ever call EnsureCreated or Migrate.
/// When a table changes, its IEntityTypeConfiguration changes with it.
/// </remarks>
public class SethikaDbContext(DbContextOptions<SethikaDbContext> options)
    : DbContext(options), ISethikaDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<PreviousPassword> PreviousPasswords => Set<PreviousPassword>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DonorProfile> DonorProfiles => Set<DonorProfile>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RoleLevel> RoleLevels => Set<RoleLevel>();
    public DbSet<RoleLevelPermission> RoleLevelPermissions => Set<RoleLevelPermission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EffectivePermission> EffectivePermissions => Set<EffectivePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SethikaDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

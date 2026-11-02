using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Institutions;
using Sethika.Domain.Onboarding;
using Sethika.Domain.Services;

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
public partial class SethikaDbContext(DbContextOptions<SethikaDbContext> options)
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
    public DbSet<FavoriteInstitution> FavoriteInstitutions => Set<FavoriteInstitution>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<RegistrationRequest> RegistrationRequests => Set<RegistrationRequest>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<InstitutionTypeRequiredDocument> InstitutionTypeRequiredDocuments => Set<InstitutionTypeRequiredDocument>();
    public DbSet<VerificationChecklistItem> VerificationChecklistItems => Set<VerificationChecklistItem>();
    public DbSet<InstitutionMember> InstitutionMembers => Set<InstitutionMember>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<InstitutionDocument> InstitutionDocuments => Set<InstitutionDocument>();
    public DbSet<InstitutionStatusChange> InstitutionStatusHistory => Set<InstitutionStatusChange>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RoleLevel> RoleLevels => Set<RoleLevel>();
    public DbSet<RoleLevelPermission> RoleLevelPermissions => Set<RoleLevelPermission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EffectivePermission> EffectivePermissions => Set<EffectivePermission>();
    public DbSet<InstitutionPermission> InstitutionPermissions => Set<InstitutionPermission>();
    public DbSet<MealSlot> MealSlots => Set<MealSlot>();
    public DbSet<BlockedDate> BlockedDates => Set<BlockedDate>();
    public DbSet<SlotInstance> SlotInstances => Set<SlotInstance>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<RecurringBooking> RecurringBookings => Set<RecurringBooking>();
    public DbSet<BookingContributor> BookingContributors => Set<BookingContributor>();
    public DbSet<MemorialPlan> MemorialPlans => Set<MemorialPlan>();
    public DbSet<MemorialRule> MemorialRules => Set<MemorialRule>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<ReligiousLeader> ReligiousLeaders => Set<ReligiousLeader>();
    public DbSet<LeaderWeeklyHours> LeaderWeeklyHours => Set<LeaderWeeklyHours>();
    public DbSet<LeaderTimeOff> LeaderTimeOff => Set<LeaderTimeOff>();
    public DbSet<LeaderTimeBlock> LeaderTimeBlocks => Set<LeaderTimeBlock>();
    public DbSet<ServiceAppointment> ServiceAppointments => Set<ServiceAppointment>();

    /// <summary>Saves, naming the unique index or constraint when one refuses (see <see cref="DuplicateKeyException"/>).</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && DuplicateKeyOf(ex) is { } key)
        {
            throw new DuplicateKeyException(key, ex);
        }
    }

    /// <inheritdoc cref="SaveChangesAsync(bool, CancellationToken)"/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && DuplicateKeyOf(ex) is { } key)
        {
            throw new DuplicateKeyException(key, ex);
        }
    }

    /// <summary>
    /// 2601 is a unique index, 2627 a unique or primary key constraint; both messages quote the
    /// name: "... with unique index 'UX_RegReq_ActiveRegNo'", "Violation of UNIQUE KEY constraint 'UQ_...'".
    /// </summary>
    private static string? DuplicateKeyOf(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql && KeyName().Match(sql.Message) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    [GeneratedRegex("(?:unique index|constraint) '([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex KeyName();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SethikaDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

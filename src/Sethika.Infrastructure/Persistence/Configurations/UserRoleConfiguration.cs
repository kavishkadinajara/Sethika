using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserRoles", "auth", table => table.IsTemporal(temporal =>
        {
            temporal.UseHistoryTable("UserRolesHistory", "auth");
            temporal.HasPeriodStart("SysStartTime").HasColumnName("SysStartTime");
            temporal.HasPeriodEnd("SysEndTime").HasColumnName("SysEndTime");
        }));

        builder.HasKey(ur => ur.Id);

        builder.Property(ur => ur.ValidFrom).HasColumnType("datetime2(3)");
        builder.Property(ur => ur.ValidTo).HasColumnType("datetime2(3)");

        builder.HasOne(ur => ur.User)
               .WithMany(u => u.UserRoles)
               .HasForeignKey(ur => ur.UserId)
               .OnDelete(DeleteBehavior.Restrict);

        // Mapped as the single-column FK only. The schema also carries a composite
        // (RoleId, InstitutionId) -> auth.Roles(Id, InstitutionId) FK that stops an
        // institution role being assigned outside its institution. EF does not need to
        // model that to write correct rows, and the database enforces it regardless.
        builder.HasOne(ur => ur.Role)
               .WithMany(r => r.UserRoles)
               .HasForeignKey(ur => ur.RoleId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

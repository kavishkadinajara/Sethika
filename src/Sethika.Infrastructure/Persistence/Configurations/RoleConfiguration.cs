using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // System-versioned. The period columns are HIDDEN in the schema, so they are
        // declared to EF but never surface as entity properties.
        builder.ToTable("Roles", "auth", table => table.IsTemporal(temporal =>
        {
            temporal.UseHistoryTable("RolesHistory", "auth");
            temporal.HasPeriodStart("SysStartTime").HasColumnName("SysStartTime");
            temporal.HasPeriodEnd("SysEndTime").HasColumnName("SysEndTime");
        }));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Scope).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.Code).HasColumnType("varchar(50)").IsRequired();
        builder.Property(r => r.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(r => r.Description).HasColumnType("nvarchar(300)");
    }
}

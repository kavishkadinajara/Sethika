using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RoleLevelPermissionConfiguration : IEntityTypeConfiguration<RoleLevelPermission>
{
    public void Configure(EntityTypeBuilder<RoleLevelPermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // LevelScope and PermissionScope exist for the composite FKs and are not mapped: the
        // application reads ceilings and never writes one.
        builder.ToTable("RoleLevelPermissions", "auth", table => table.IsTemporal(temporal =>
        {
            temporal.UseHistoryTable("RoleLevelPermissionsHistory", "auth");
            temporal.HasPeriodStart("SysStartTime").HasColumnName("SysStartTime");
            temporal.HasPeriodEnd("SysEndTime").HasColumnName("SysEndTime");
        }));

        builder.HasKey(c => new { c.RoleLevelId, c.PermissionId });
    }
}

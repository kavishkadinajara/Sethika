using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RolePermissions", "auth", table => table.IsTemporal(temporal =>
        {
            temporal.UseHistoryTable("RolePermissionsHistory", "auth");
            temporal.HasPeriodStart("SysStartTime").HasColumnName("SysStartTime");
            temporal.HasPeriodEnd("SysEndTime").HasColumnName("SysEndTime");
        }));

        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
    }
}

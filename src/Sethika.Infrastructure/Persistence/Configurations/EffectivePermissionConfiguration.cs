using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class EffectivePermissionConfiguration : IEntityTypeConfiguration<EffectivePermission>
{
    public void Configure(EntityTypeBuilder<EffectivePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToView("vw_EffectivePermissions", "auth");
        builder.HasNoKey();
        builder.Property(e => e.PermissionCode).HasColumnType("varchar(150)").IsRequired();
    }
}

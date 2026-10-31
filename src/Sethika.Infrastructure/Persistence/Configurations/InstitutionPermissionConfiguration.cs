using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionPermissionConfiguration : IEntityTypeConfiguration<InstitutionPermission>
{
    public void Configure(EntityTypeBuilder<InstitutionPermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToView("vw_InstitutionPermissions", "auth");
        builder.HasNoKey();
        builder.Property(e => e.PermissionCode).HasColumnType("varchar(150)").IsRequired();
    }
}

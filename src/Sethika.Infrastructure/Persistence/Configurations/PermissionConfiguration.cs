using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Permissions", "auth");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasColumnType("varchar(150)").IsRequired();
        builder.Property(p => p.Scope).HasColumnType("varchar(20)").IsRequired();
    }
}

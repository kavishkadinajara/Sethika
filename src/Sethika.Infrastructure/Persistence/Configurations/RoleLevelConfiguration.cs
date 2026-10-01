using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RoleLevelConfiguration : IEntityTypeConfiguration<RoleLevel>
{
    public void Configure(EntityTypeBuilder<RoleLevel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleLevels", "auth");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Scope).HasColumnType("varchar(20)").IsRequired();
        builder.Property(l => l.Code).HasColumnType("varchar(30)").IsRequired();
        builder.Property(l => l.Name).HasColumnType("nvarchar(100)").IsRequired();
    }
}

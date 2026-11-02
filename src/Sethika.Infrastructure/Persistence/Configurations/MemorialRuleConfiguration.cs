using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class MemorialRuleConfiguration : IEntityTypeConfiguration<MemorialRule>
{
    public void Configure(EntityTypeBuilder<MemorialRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Reference data from 13_MemorialDrafts.sql; read, never written.
        builder.ToTable("MemorialRules", "core");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Code).HasColumnType("varchar(40)").IsRequired();
        builder.Property(r => r.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(r => r.NameSi).HasColumnType("nvarchar(100)");
        builder.Property(r => r.NameTa).HasColumnType("nvarchar(100)");
    }
}

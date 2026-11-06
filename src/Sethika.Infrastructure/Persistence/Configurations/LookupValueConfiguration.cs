using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;

namespace Sethika.Infrastructure.Persistence.Configurations;

/// <summary>Seeded lists (plan 09, M20): read, never written by the application.</summary>
internal sealed class LookupValueConfiguration : IEntityTypeConfiguration<LookupValue>
{
    public void Configure(EntityTypeBuilder<LookupValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LookupValues", "core");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Code).HasColumnType("varchar(50)").IsRequired();
        builder.Property(v => v.Name).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(v => v.NameSi).HasColumnType("nvarchar(150)");
        builder.Property(v => v.NameTa).HasColumnType("nvarchar(150)");
    }
}

internal sealed class LookupTypeConfiguration : IEntityTypeConfiguration<LookupType>
{
    public void Configure(EntityTypeBuilder<LookupType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LookupTypes", "core");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasColumnType("varchar(50)").IsRequired();
        builder.Property(t => t.Name).HasColumnType("nvarchar(100)").IsRequired();
    }
}

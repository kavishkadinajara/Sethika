using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security: anyone may read a service type (an institution's page lists them), only
        // its institution may write one, as with meal slots (Phase E, V9).
        builder.ToTable("ServiceTypes", "booking");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(t => t.NameSi).HasColumnType("nvarchar(100)");
        builder.Property(t => t.NameTa).HasColumnType("nvarchar(100)");
        builder.Property(t => t.Description).HasColumnType("nvarchar(300)");
        builder.Property(t => t.LocationMode).HasColumnType("varchar(15)").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.RowVersion).IsRowVersion();
    }
}

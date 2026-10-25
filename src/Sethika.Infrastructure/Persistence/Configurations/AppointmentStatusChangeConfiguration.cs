using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentStatusChangeConfiguration : IEntityTypeConfiguration<AppointmentStatusChange>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AppointmentStatusHistory", "booking");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasColumnType("varchar(15)");
        builder.Property(h => h.ToStatus).HasColumnType("varchar(15)").IsRequired();
        builder.Property(h => h.ChangedAt).HasColumnType("datetime2(3)");
        builder.Property(h => h.Comment).HasColumnType("nvarchar(500)");
    }
}

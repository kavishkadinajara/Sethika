using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class BookingStatusChangeConfiguration : IEntityTypeConfiguration<BookingStatusChange>
{
    public void Configure(EntityTypeBuilder<BookingStatusChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("BookingStatusHistory", "booking");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasColumnType("varchar(20)");
        builder.Property(h => h.ToStatus).HasColumnType("varchar(20)").IsRequired();
        builder.Property(h => h.ChangedAt).HasColumnType("datetime2(3)");
        builder.Property(h => h.Comment).HasColumnType("nvarchar(500)");
    }
}

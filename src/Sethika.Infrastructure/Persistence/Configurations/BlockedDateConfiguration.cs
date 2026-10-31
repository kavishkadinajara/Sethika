using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class BlockedDateConfiguration : IEntityTypeConfiguration<BlockedDate>
{
    public void Configure(EntityTypeBuilder<BlockedDate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security: public read, tenant-only insert and delete, like meal slots.
        builder.ToTable("BlockedDates", "booking");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.DateFrom).HasColumnType("date");
        builder.Property(b => b.DateTo).HasColumnType("date");
        builder.Property(b => b.Reason).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnType("datetime2(3)");
    }
}

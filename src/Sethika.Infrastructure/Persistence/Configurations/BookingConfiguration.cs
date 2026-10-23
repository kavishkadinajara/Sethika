using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner): the institution sees its bookings and the donor
        // sees theirs, and neither can write a row the other would then own.
        builder.ToTable("Bookings", "booking");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BookingNo).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.DonorName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(b => b.DonorPhone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.FulfilmentMode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.InMemoryOf).HasColumnType("nvarchar(200)");
        builder.Property(b => b.Notes).HasColumnType("nvarchar(500)");
        builder.Property(b => b.Source).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(b => b.DecidedAt).HasColumnType("datetime2(3)");
        builder.Property(b => b.RejectReason).HasColumnType("nvarchar(300)");
        builder.Property(b => b.CancelledAt).HasColumnType("datetime2(3)");
        builder.Property(b => b.CancelReason).HasColumnType("nvarchar(300)");
        builder.Property(b => b.CompletedAt).HasColumnType("datetime2(3)");
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasMany(b => b.StatusHistory).WithOne().HasForeignKey(h => h.BookingId);
    }
}

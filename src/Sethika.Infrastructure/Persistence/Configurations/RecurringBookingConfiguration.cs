using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RecurringBookingConfiguration : IEntityTypeConfiguration<RecurringBooking>
{
    public void Configure(EntityTypeBuilder<RecurringBooking> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner), as on bookings: the donor sees their yearly
        // bookings and the institution the ones aimed at it.
        builder.ToTable("RecurringBookings", "booking");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.InMemoryOf).HasColumnType("nvarchar(200)");
        builder.Property(r => r.FulfilmentMode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2(3)");

        builder.Ignore(r => r.NextYear);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class StandingDayChangeConfiguration : IEntityTypeConfiguration<StandingDayChange>
{
    public void Configure(EntityTypeBuilder<StandingDayChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Tenant), as its sponsorship (plan 08, K16).
        builder.ToTable("StandingExceptions", "booking");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).HasColumnType("varchar(10)").IsRequired();
        builder.Property(e => e.Reason).HasColumnType("nvarchar(300)");
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");
    }
}

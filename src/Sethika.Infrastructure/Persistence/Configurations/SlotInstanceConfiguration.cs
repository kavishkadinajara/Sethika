using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class SlotInstanceConfiguration : IEntityTypeConfiguration<SlotInstance>
{
    public void Configure(EntityTypeBuilder<SlotInstance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // No Row-Level Security, on purpose (plan 03, C8): a donor's connection updates another
        // tenant's counters. The CHECKs bound what any connection can do to them.
        builder.ToTable("SlotInstances", "booking");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.SlotDate).HasColumnType("date");
        builder.Property(i => i.Status).HasColumnType("varchar(10)").IsRequired();
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.Ignore(i => i.RemainingSponsors);
        builder.Ignore(i => i.RemainingHeadcount);
    }
}

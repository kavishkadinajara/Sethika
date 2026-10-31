using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class LeaderTimeBlockConfiguration : IEntityTypeConfiguration<LeaderTimeBlock>
{
    public void Configure(EntityTypeBuilder<LeaderTimeBlock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The primary key is the overlap guarantee (Phase E, V2): a second block for the same leader
        // at the same quarter-hour is a 2627 on PK_LeaderTimeBlocks.
        builder.ToTable("LeaderTimeBlocks", "booking");
        builder.HasKey(b => new { b.LeaderId, b.BlockStart });

        builder.Property(b => b.BlockStart).HasColumnType("datetime2(3)");
        builder.Property(b => b.Kind).HasColumnType("varchar(12)").IsRequired();
    }
}

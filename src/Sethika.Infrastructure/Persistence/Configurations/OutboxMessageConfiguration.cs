using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxMessages", "core");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.EventType).HasColumnType("varchar(100)").IsRequired();
        builder.Property(m => m.AggregateType).HasColumnType("varchar(50)").IsRequired();
        builder.Property(m => m.AggregateId).HasColumnType("varchar(50)").IsRequired();
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(m => m.CorrelationId).HasColumnType("varchar(64)");
        builder.Property(m => m.LastError).HasColumnType("nvarchar(1000)");
        builder.Property(m => m.OccurredAt).HasColumnType("datetime2(3)");
        builder.Property(m => m.ProcessedAt).HasColumnType("datetime2(3)");
    }
}

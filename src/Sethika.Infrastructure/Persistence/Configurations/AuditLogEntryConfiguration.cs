using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditLogs", "audit");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasColumnType("varchar(50)").IsRequired();
        builder.Property(a => a.EntityName).HasColumnType("varchar(100)").IsRequired();
        builder.Property(a => a.EntityId).HasColumnType("varchar(50)").IsRequired();
        builder.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.IpAddress).HasColumnType("varchar(45)");
        builder.Property(a => a.UserAgent).HasColumnType("nvarchar(400)");
        builder.Property(a => a.CorrelationId).HasColumnType("varchar(64)");
        builder.Property(a => a.OccurredAt).HasColumnType("datetime2(3)");
    }
}

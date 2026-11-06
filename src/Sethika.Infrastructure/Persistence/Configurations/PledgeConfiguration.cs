using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Giving;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class PledgeConfiguration : IEntityTypeConfiguration<Pledge>
{
    public void Configure(EntityTypeBuilder<Pledge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner): its institution and its donor. A donor's own
        // pledge and cancel run as the platform all the same, because they move a need's counter (M14).
        builder.ToTable("Pledges", "giving");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Quantity).HasColumnType("decimal(10,2)");
        builder.Property(p => p.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(p => p.ReceivedAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.ReceivedQuantity).HasColumnType("decimal(10,2)");
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.CancelledAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.CancelReason).HasColumnType("nvarchar(300)");
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}

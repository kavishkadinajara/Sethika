using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Giving;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner reads, sec.fn_Tenant writes, plan 09). The trigger
        // (M7) refuses any change but a void: declared so EF Core saves without a bare OUTPUT clause,
        // which SQL Server refuses on a table with triggers.
        builder.ToTable("Donations", "giving", table => table.HasTrigger("trg_Donations_Immutable"));
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ReceiptNo).HasColumnType("varchar(30)").IsRequired();
        builder.Property(d => d.DonorName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(d => d.DonorPhone).HasColumnType("varchar(20)");
        builder.Property(d => d.Kind).HasColumnType("varchar(10)").IsRequired();
        builder.Property(d => d.Amount).HasColumnType("decimal(12,2)");
        builder.Property(d => d.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(d => d.ItemDescription).HasColumnType("nvarchar(500)");
        builder.Property(d => d.Quantity).HasColumnType("decimal(10,2)");
        builder.Property(d => d.EstimatedValue).HasColumnType("decimal(12,2)");
        builder.Property(d => d.Note).HasColumnType("nvarchar(300)");
        builder.Property(d => d.DonatedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.RecordedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.VoidReason).HasColumnType("nvarchar(300)");
        builder.Property(d => d.VoidedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.RowVersion).IsRowVersion();
    }
}

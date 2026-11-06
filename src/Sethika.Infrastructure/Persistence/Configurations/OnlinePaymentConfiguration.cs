using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Giving;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class OnlinePaymentConfiguration : IEntityTypeConfiguration<OnlinePayment>
{
    public void Configure(EntityTypeBuilder<OnlinePayment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner reads, sec.fn_Tenant writes, plan 09, M34).
        builder.ToTable("OnlinePayments", "giving");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.MerchantId).HasColumnType("varchar(30)").IsRequired();
        builder.Property(p => p.Amount).HasColumnType("decimal(12,2)");
        builder.Property(p => p.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(p => p.PayHerePaymentId).HasColumnType("varchar(30)");
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.NotifiedAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}

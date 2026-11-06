using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Giving;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class PaymentMerchantConfiguration : IEntityTypeConfiguration<PaymentMerchant>
{
    public void Configure(EntityTypeBuilder<PaymentMerchant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Tenant): the institution alone; a payment reads it as the platform (M10).
        builder.ToTable("PaymentMerchants", "giving");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Provider).HasColumnType("varchar(20)").IsRequired();
        builder.Property(m => m.MerchantId).HasColumnType("varchar(30)").IsRequired();
        builder.Property(m => m.SecretKeyId).HasColumnType("varchar(20)").IsRequired();
        builder.Property(m => m.SecretNonce).HasColumnType("binary(12)").IsRequired();
        builder.Property(m => m.SecretCipher).HasColumnType("varbinary(256)").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(m => m.RowVersion).IsRowVersion();
    }
}

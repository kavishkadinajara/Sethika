using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Giving;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class NeedConfiguration : IEntityTypeConfiguration<Need>
{
    public void Configure(EntityTypeBuilder<Need> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Public read, tenant writes (sec.fn_Tenant, M14): a donor's session cannot write it at all.
        builder.ToTable("Needs", "giving");
        builder.HasKey(n => n.Id);

        builder.Ignore(n => n.OpenPledged);
        builder.Ignore(n => n.IsEditable);

        builder.Property(n => n.Title).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(n => n.Description).HasColumnType("nvarchar(1000)");
        builder.Property(n => n.QuantityNeeded).HasColumnType("decimal(10,2)");
        builder.Property(n => n.QuantityPledged).HasColumnType("decimal(10,2)");
        builder.Property(n => n.QuantityReceived).HasColumnType("decimal(10,2)");
        builder.Property(n => n.Priority).HasColumnType("varchar(10)").IsRequired();
        builder.Property(n => n.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(n => n.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(n => n.RowVersion).IsRowVersion();
    }
}

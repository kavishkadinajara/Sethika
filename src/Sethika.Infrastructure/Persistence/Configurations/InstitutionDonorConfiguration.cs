using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionDonorConfiguration : IEntityTypeConfiguration<InstitutionDonor>
{
    public void Configure(EntityTypeBuilder<InstitutionDonor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Tenant): the donor book is its institution's alone (plan 08, K1).
        builder.ToTable("InstitutionDonors", "org");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FullName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(d => d.Phone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(d => d.Notes).HasColumnType("nvarchar(500)");
        builder.Property(d => d.Language).HasColumnType("char(2)").IsRequired();
        builder.Property(d => d.TextConsentAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.RowVersion).IsRowVersion();
    }
}

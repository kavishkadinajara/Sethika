using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionStatusChangeConfiguration : IEntityTypeConfiguration<InstitutionStatusChange>
{
    public void Configure(EntityTypeBuilder<InstitutionStatusChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InstitutionStatusHistory", "org");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasColumnType("varchar(20)");
        builder.Property(h => h.ToStatus).HasColumnType("varchar(20)").IsRequired();
        builder.Property(h => h.Reason).HasColumnType("nvarchar(1000)");
        builder.Property(h => h.ChangedAt).HasColumnType("datetime2(3)");

        builder.HasOne<Institution>().WithMany().HasForeignKey(h => h.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}

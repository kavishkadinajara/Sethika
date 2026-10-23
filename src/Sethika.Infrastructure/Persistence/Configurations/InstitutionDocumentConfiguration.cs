using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionDocumentConfiguration : IEntityTypeConfiguration<InstitutionDocument>
{
    public void Configure(EntityTypeBuilder<InstitutionDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InstitutionDocuments", "org");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ReferenceNo).HasColumnType("varchar(80)");
        builder.Property(d => d.IssuedDate).HasColumnType("date");
        builder.Property(d => d.ExpiryDate).HasColumnType("date");
        builder.Property(d => d.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne<Institution>().WithMany().HasForeignKey(d => d.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Content (nvarchar(max)) is left unmapped on purpose - see LegalDocument.
        builder.ToTable("LegalDocuments", "core");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasColumnType("varchar(40)").IsRequired();
        builder.Property(d => d.Version).HasColumnType("varchar(10)").IsRequired();
        builder.Property(d => d.Language).HasColumnType("char(2)").IsRequired();
        builder.Property(d => d.Title).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(d => d.ContentSha256).HasColumnType("char(64)").IsRequired();
        builder.Property(d => d.EffectiveFrom).HasColumnType("datetime2(3)");
    }
}

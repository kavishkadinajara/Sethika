using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

/// <summary>Reference data from <c>12_OnboardingDrafts.sql</c>; commands read it, never write it.</summary>
internal sealed class InstitutionTypeRequiredDocumentConfiguration : IEntityTypeConfiguration<InstitutionTypeRequiredDocument>
{
    public void Configure(EntityTypeBuilder<InstitutionTypeRequiredDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InstitutionTypeRequiredDocuments", "org");
        builder.HasKey(r => new { r.InstitutionTypeId, r.DocumentTypeId });

        builder.Property(r => r.Instructions).HasColumnType("nvarchar(400)");
    }
}

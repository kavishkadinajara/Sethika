using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

/// <summary>Reference data from <c>12_OnboardingDrafts.sql</c>; commands read it, never write it.</summary>
internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DocumentTypes", "org");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasColumnType("varchar(50)").IsRequired();
        builder.Property(t => t.Name).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(t => t.Description).HasColumnType("nvarchar(400)");
        builder.Property(t => t.AllowedExtensions).HasColumnType("varchar(100)").IsRequired();
    }
}

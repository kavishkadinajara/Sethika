using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

/// <summary>Reference data from <c>12_OnboardingDrafts.sql</c>; commands read it, never write it.</summary>
internal sealed class VerificationChecklistItemConfiguration : IEntityTypeConfiguration<VerificationChecklistItem>
{
    public void Configure(EntityTypeBuilder<VerificationChecklistItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VerificationChecklistItems", "org");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Code).HasColumnType("varchar(50)").IsRequired();
        builder.Property(i => i.Question).HasColumnType("nvarchar(300)").IsRequired();
    }
}

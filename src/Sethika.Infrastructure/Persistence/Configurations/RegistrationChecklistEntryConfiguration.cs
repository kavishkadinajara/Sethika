using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationChecklistEntryConfiguration : IEntityTypeConfiguration<RegistrationChecklistEntry>
{
    public void Configure(EntityTypeBuilder<RegistrationChecklistEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistrationRequestChecklist", "org");
        builder.HasKey(e => new { e.RequestId, e.ChecklistItemId });

        builder.Property(e => e.CheckedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.Remarks).HasColumnType("nvarchar(500)");

        builder.HasOne<VerificationChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

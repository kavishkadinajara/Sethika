using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationClarificationConfiguration : IEntityTypeConfiguration<RegistrationClarification>
{
    public void Configure(EntityTypeBuilder<RegistrationClarification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistrationRequestClarifications", "org");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Question).HasColumnType("nvarchar(1000)").IsRequired();
        builder.Property(c => c.AskedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.Response).HasColumnType("nvarchar(2000)");
        builder.Property(c => c.RespondedAt).HasColumnType("datetime2(3)");

        builder.Ignore(c => c.IsOpen);
    }
}

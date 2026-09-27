using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class DonorProfileConfiguration : IEntityTypeConfiguration<DonorProfile>
{
    public void Configure(EntityTypeBuilder<DonorProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DonorProfiles", "auth");

        // The key is the user's id, supplied by the caller - not generated.
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();

        builder.Property(p => p.CountryCode).HasColumnType("char(2)").IsRequired();
        builder.Property(p => p.DefaultDisplayName).HasColumnType("nvarchar(150)");

        // Declared so EF orders the insert after its user's within one SaveChanges; the database
        // enforces the FK either way.
        builder.HasOne<User>().WithOne().HasForeignKey<DonorProfile>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserConsents", "auth");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.IpAddress).HasColumnType("varchar(45)");
        builder.Property(c => c.UserAgent).HasColumnType("nvarchar(400)");
        builder.Property(c => c.AcceptedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.WithdrawnAt).HasColumnType("datetime2(3)");

        // Declared so EF orders the insert after its user's within one SaveChanges; the database
        // enforces the FK either way.
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

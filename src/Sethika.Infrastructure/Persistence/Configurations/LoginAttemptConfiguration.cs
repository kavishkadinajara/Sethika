using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LoginHistory", "auth");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.LoginName).HasColumnType("nvarchar(256)").IsRequired();
        builder.Property(a => a.FailureReason).HasColumnType("varchar(50)");
        builder.Property(a => a.IpAddress).HasColumnType("varchar(45)");
        builder.Property(a => a.UserAgent).HasColumnType("nvarchar(400)");
        builder.Property(a => a.AttemptedAt).HasColumnType("datetime2(3)");

        // Declared so EF orders the insert after its user's within one SaveChanges; the database
        // enforces the FK either way.
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
    }
}

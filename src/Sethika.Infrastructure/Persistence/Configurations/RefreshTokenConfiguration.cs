using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens", "auth");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasColumnType("char(64)").IsRequired();
        builder.Property(t => t.JwtId).HasColumnType("varchar(64)").IsRequired();
        builder.Property(t => t.DeviceInfo).HasColumnType("nvarchar(200)");
        builder.Property(t => t.CreatedByIp).HasColumnType("varchar(45)");
        builder.Property(t => t.RevokedReason).HasColumnType("nvarchar(100)");

        builder.Property(t => t.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.ExpiresAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.RevokedAt).HasColumnType("datetime2(3)");

        // Declared so EF orders the insert after its user's within one SaveChanges; the database
        // enforces the FK either way.
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class PreviousPasswordConfiguration : IEntityTypeConfiguration<PreviousPassword>
{
    public void Configure(EntityTypeBuilder<PreviousPassword> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PasswordHistory", "auth");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PasswordHash).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

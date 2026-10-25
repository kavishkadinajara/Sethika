using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class LeaderTimeOffConfiguration : IEntityTypeConfiguration<LeaderTimeOff>
{
    public void Configure(EntityTypeBuilder<LeaderTimeOff> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LeaderTimeOff", "org");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.StartsAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.EndsAt).HasColumnType("datetime2(3)");
        builder.Property(t => t.Reason).HasColumnType("nvarchar(200)");
        builder.Property(t => t.CreatedAt).HasColumnType("datetime2(3)");
    }
}

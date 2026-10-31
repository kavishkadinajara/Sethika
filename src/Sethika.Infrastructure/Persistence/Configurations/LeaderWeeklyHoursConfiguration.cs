using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class LeaderWeeklyHoursConfiguration : IEntityTypeConfiguration<LeaderWeeklyHours>
{
    public void Configure(EntityTypeBuilder<LeaderWeeklyHours> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LeaderWeeklyHours", "org");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.StartTime).HasColumnType("time(0)");
        builder.Property(h => h.EndTime).HasColumnType("time(0)");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class MealSlotSeasonConfiguration : IEntityTypeConfiguration<MealSlotSeason>
{
    public void Configure(EntityTypeBuilder<MealSlotSeason> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MealSlotSeasons", "booking");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(s => s.DateFrom).HasColumnType("date");
        builder.Property(s => s.DateTo).HasColumnType("date");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class MealSlotConfiguration : IEntityTypeConfiguration<MealSlot>
{
    public void Configure(EntityTypeBuilder<MealSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security: anyone may read a slot (availability is public), only its institution
        // may write one (sec.TenantIsolationPolicy's BLOCK predicates).
        builder.ToTable("MealSlots", "booking");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(s => s.NameSi).HasColumnType("nvarchar(100)");
        builder.Property(s => s.NameTa).HasColumnType("nvarchar(100)");
        builder.Property(s => s.StartTime).HasColumnType("time(0)");
        builder.Property(s => s.EndTime).HasColumnType("time(0)");
        builder.Property(s => s.BookingMode).HasColumnType("varchar(10)").IsRequired();
        builder.Property(s => s.EstimatedCostPerHead).HasColumnType("decimal(10,2)");
        builder.Property(s => s.AllowedFulfilment).HasColumnType("varchar(40)").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasMany(s => s.Seasons).WithOne().HasForeignKey(s => s.MealSlotId).OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class MemorialPlanConfiguration : IEntityTypeConfiguration<MemorialPlan>
{
    public void Configure(EntityTypeBuilder<MemorialPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Owner): the donor alone; no institution ever sees a plan.
        builder.ToTable("MemorialPlans", "booking");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DeceasedName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(p => p.Notes).HasColumnType("nvarchar(500)");
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");
    }
}

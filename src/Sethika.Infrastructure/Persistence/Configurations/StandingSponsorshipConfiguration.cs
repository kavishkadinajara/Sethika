using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class StandingSponsorshipConfiguration : IEntityTypeConfiguration<StandingSponsorship>
{
    public void Configure(EntityTypeBuilder<StandingSponsorship> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Tenant): the institution's alone (plan 08, K16). A linked sponsor
        // reaches their days as their own bookings, never through this table.
        builder.ToTable("StandingSponsorships", "booking");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Pattern).HasColumnType("varchar(10)").IsRequired();
        builder.Property(s => s.FulfilmentMode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(s => s.InMemoryOf).HasColumnType("nvarchar(200)");
        builder.Property(s => s.Notes).HasColumnType("nvarchar(500)");
        builder.Property(s => s.Status).HasColumnType("varchar(10)").IsRequired();
        builder.Property(s => s.EndedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.Ignore(s => s.IsActive);
    }
}

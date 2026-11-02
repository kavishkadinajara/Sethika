using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class DayRequestConfiguration : IEntityTypeConfiguration<DayRequest>
{
    public void Configure(EntityTypeBuilder<DayRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner): the institution asked, and the donor who asked (K16).
        builder.ToTable("DayRequests", "booking");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestNo).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.FulfilmentMode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.Note).HasColumnType("nvarchar(300)");
        builder.Property(r => r.Status).HasColumnType("varchar(10)").IsRequired();
        builder.Property(r => r.DeclineReason).HasColumnType("nvarchar(300)");
        builder.Property(r => r.DecidedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.Ignore(r => r.IsOpen);
    }
}

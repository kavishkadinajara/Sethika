using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class ServiceAppointmentConfiguration : IEntityTypeConfiguration<ServiceAppointment>
{
    public void Configure(EntityTypeBuilder<ServiceAppointment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_TenantOrOwner): the institution sees its appointments and the
        // donor sees theirs, as with bookings (Phase E, V9).
        builder.ToTable("ServiceAppointments", "booking");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AppointmentNo).HasColumnType("varchar(20)").IsRequired();
        builder.Property(a => a.DonorName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(a => a.DonorPhone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(a => a.StartsAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.EndsAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.LocationMode).HasColumnType("varchar(15)").IsRequired();
        builder.Property(a => a.Address).HasColumnType("nvarchar(300)");
        builder.Property(a => a.Occasion).HasColumnType("nvarchar(200)");
        builder.Property(a => a.DonorNote).HasColumnType("nvarchar(500)");
        builder.Property(a => a.Status).HasColumnType("varchar(15)").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.DecidedAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.RejectReason).HasColumnType("nvarchar(300)");
        builder.Property(a => a.CancelledAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.CancelReason).HasColumnType("nvarchar(300)");
        builder.Property(a => a.CompletedAt).HasColumnType("datetime2(3)");
        builder.Property(a => a.RowVersion).IsRowVersion();

        // LiveLeaders is a filter over Leaders, not a second relationship: without this, EF's
        // conventions map it as one, with a shadow ServiceAppointmentId column.
        builder.Ignore(a => a.LiveLeaders);
        builder.HasMany(a => a.Leaders).WithOne().HasForeignKey(l => l.AppointmentId);
        builder.HasMany(a => a.StatusHistory).WithOne().HasForeignKey(h => h.AppointmentId);
    }
}

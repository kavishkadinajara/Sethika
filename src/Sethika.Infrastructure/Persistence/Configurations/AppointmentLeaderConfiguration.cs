using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentLeaderConfiguration : IEntityTypeConfiguration<AppointmentLeader>
{
    public void Configure(EntityTypeBuilder<AppointmentLeader> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AppointmentLeaders", "booking");
        builder.HasKey(l => new { l.AppointmentId, l.LeaderId });

        builder.Property(l => l.Response).HasColumnType("varchar(10)").IsRequired();
        builder.Property(l => l.AssignedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.RespondedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.DeclineReason).HasColumnType("nvarchar(300)");

        builder.Ignore(l => l.IsLive);
    }
}

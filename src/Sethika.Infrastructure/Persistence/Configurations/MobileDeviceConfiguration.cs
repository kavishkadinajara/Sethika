using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Notifications;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class MobileDeviceConfiguration : IEntityTypeConfiguration<MobileDevice>
{
    public void Configure(EntityTypeBuilder<MobileDevice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MobileDevices", "notify");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ExpoPushToken).HasMaxLength(200).IsUnicode(false);
        builder.Property(d => d.Platform).HasMaxLength(10).IsUnicode(false);
        builder.Property(d => d.AppVersion).HasMaxLength(20).IsUnicode(false);
        builder.Property(d => d.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.LastSeenAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.DisabledAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.DisabledReason).HasMaxLength(100);
    }
}

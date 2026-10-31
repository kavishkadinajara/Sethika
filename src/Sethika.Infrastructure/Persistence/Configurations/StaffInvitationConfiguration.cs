using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class StaffInvitationConfiguration : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(EntityTypeBuilder<StaffInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StaffInvitations", "org");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InviteeName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(i => i.InviteePhone).HasColumnType("varchar(20)");
        builder.Property(i => i.InviteeEmail).HasColumnType("nvarchar(256)");
        builder.Property(i => i.TokenHash).HasColumnType("char(64)").IsRequired();
        builder.Property(i => i.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(i => i.InvitedAt).HasColumnType("datetime2(3)");
        builder.Property(i => i.ExpiresAt).HasColumnType("datetime2(3)");
        builder.Property(i => i.AcceptedAt).HasColumnType("datetime2(3)");
        builder.Property(i => i.ClosedAt).HasColumnType("datetime2(3)");
    }
}

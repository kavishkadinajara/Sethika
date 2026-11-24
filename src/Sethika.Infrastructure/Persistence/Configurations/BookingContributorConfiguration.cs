using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Bookings;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class BookingContributorConfiguration : IEntityTypeConfiguration<BookingContributor>
{
    public void Configure(EntityTypeBuilder<BookingContributor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Contributor): the institution, the person who answered and the
        // donor who invited. Phone is never written (H16), so it is not mapped.
        builder.ToTable("BookingContributors", "booking");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(c => c.ContributionNote).HasColumnType("nvarchar(200)");
        builder.Property(c => c.InviteTokenHash).HasColumnType("char(64)").IsRequired();
        builder.Property(c => c.Status).HasColumnType("varchar(10)").IsRequired();
        builder.Property(c => c.JoinedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.DeclinedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.InvitedAt).HasColumnType("datetime2(3)");
    }
}

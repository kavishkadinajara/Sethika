using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class FavoriteInstitutionConfiguration : IEntityTypeConfiguration<FavoriteInstitution>
{
    public void Configure(EntityTypeBuilder<FavoriteInstitution> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Owner): the donor alone (plan 07, H12).
        builder.ToTable("FavoriteInstitutions", "auth");
        builder.HasKey(f => new { f.UserId, f.InstitutionId });

        builder.Property(f => f.AddedAt).HasColumnType("datetime2(3)");
    }
}

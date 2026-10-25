using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Services;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class ReligiousLeaderConfiguration : IEntityTypeConfiguration<ReligiousLeader>
{
    public void Configure(EntityTypeBuilder<ReligiousLeader> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (sec.fn_Tenant): the roster is its institution's alone.
        builder.ToTable("ReligiousLeaders", "org");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.DisplayName).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(l => l.NameSi).HasColumnType("nvarchar(100)");
        builder.Property(l => l.NameTa).HasColumnType("nvarchar(100)");
        builder.Property(l => l.Title).HasColumnType("nvarchar(40)");
        builder.Property(l => l.Phone).HasColumnType("varchar(20)");
        builder.Property(l => l.Language).HasColumnType("char(2)").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.RowVersion).IsRowVersion();
    }
}

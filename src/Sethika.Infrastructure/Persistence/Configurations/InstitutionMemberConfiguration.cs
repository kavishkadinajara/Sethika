using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionMemberConfiguration : IEntityTypeConfiguration<InstitutionMember>
{
    public void Configure(EntityTypeBuilder<InstitutionMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InstitutionMembers", "org");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Designation).HasColumnType("nvarchar(100)");
        builder.Property(m => m.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(m => m.JoinedAt).HasColumnType("datetime2(3)");
        builder.Property(m => m.RemovedAt).HasColumnType("datetime2(3)");

        // Declared so EF inserts a member after the institution it belongs to within one SaveChanges.
        builder.HasOne<Institution>().WithMany().HasForeignKey(m => m.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}

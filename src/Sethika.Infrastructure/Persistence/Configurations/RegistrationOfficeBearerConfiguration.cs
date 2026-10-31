using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationOfficeBearerConfiguration : IEntityTypeConfiguration<RegistrationOfficeBearer>
{
    public void Configure(EntityTypeBuilder<RegistrationOfficeBearer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistrationRequestOfficeBearers", "org");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.FullName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(b => b.NicNumber).HasColumnType("varchar(12)").IsRequired();
        builder.Property(b => b.Designation).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(b => b.Phone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.Email).HasColumnType("nvarchar(256)");

        // UQ_OfficeBearer_Nic. Replacing the set must delete the old rows before inserting one that
        // names the same person. EF Core 10 already does (removing this survived
        // ReplacingTheOfficeBearersCanNameTheSamePersonAgain); declaring it means the order rests on
        // EF knowing the index, not on its default ordering.
        builder.HasIndex(b => new { b.RequestId, b.NicNumber }).IsUnique().HasDatabaseName("UQ_OfficeBearer_Nic");
    }
}

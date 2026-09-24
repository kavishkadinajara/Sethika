using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Institutions;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Institutions", "org");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InstitutionCode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(i => i.Slug).HasColumnType("varchar(150)").IsRequired();
        builder.Property(i => i.Name).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(i => i.NameSi).HasColumnType("nvarchar(200)");
        builder.Property(i => i.NameTa).HasColumnType("nvarchar(200)");
        builder.Property(i => i.OfficialRegistrationNo).HasColumnType("varchar(50)").IsRequired();
        builder.Property(i => i.Description).HasColumnType("nvarchar(2000)");
        builder.Property(i => i.AddressLine1).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(i => i.AddressLine2).HasColumnType("nvarchar(200)");
        builder.Property(i => i.City).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(i => i.Phone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(i => i.Email).HasColumnType("nvarchar(256)");
        builder.Property(i => i.DietaryNotes).HasColumnType("nvarchar(500)");
        builder.Property(i => i.Status).HasColumnType("varchar(20)").IsRequired();

        builder.Property(i => i.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(i => i.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(i => i.NextReviewDue).HasColumnType("date");

        builder.Property(i => i.ApprovedAt).HasColumnType("datetime2(3)");
        builder.Property(i => i.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnType("datetime2(3)");

        builder.Property(i => i.RowVersion).IsRowVersion();

        // org.Institutions.Location is a PERSISTED geography computed column. It stays
        // unmapped on purpose: nothing reads it through EF yet, and mapping it would
        // pull NetTopologySuite into the build. SQL Server computes it on insert, so
        // leaving it out of the model is safe.
    }
}

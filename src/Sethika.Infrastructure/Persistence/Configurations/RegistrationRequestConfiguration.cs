using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationRequestConfiguration : IEntityTypeConfiguration<RegistrationRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Row-Level Security (plan 02, B2): only the applicant, the platform and the institution the
        // application created can read or write this row, whatever the query says.
        builder.ToTable("RegistrationRequests", "org");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestNo).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.ProposedName).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(r => r.NameSi).HasColumnType("nvarchar(200)");
        builder.Property(r => r.NameTa).HasColumnType("nvarchar(200)");
        builder.Property(r => r.OfficialRegistrationNo).HasColumnType("varchar(50)").IsRequired();
        builder.Property(r => r.RegisteringAuthority).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(r => r.RegistrationDate).HasColumnType("date");
        builder.Property(r => r.Description).HasColumnType("nvarchar(2000)");

        builder.Property(r => r.AddressLine1).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(r => r.AddressLine2).HasColumnType("nvarchar(200)");
        builder.Property(r => r.City).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(r => r.GnDivision).HasColumnType("nvarchar(100)");
        builder.Property(r => r.PostalCode).HasColumnType("varchar(10)");
        builder.Property(r => r.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(r => r.Longitude).HasColumnType("decimal(9,6)");

        builder.Property(r => r.OfficePhone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.OfficeEmail).HasColumnType("nvarchar(256)");
        builder.Property(r => r.Website).HasColumnType("nvarchar(200)");

        builder.Property(r => r.ApplicantFullName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(r => r.ApplicantNic).HasColumnType("varchar(12)").IsRequired();
        builder.Property(r => r.ApplicantDesignation).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(r => r.ApplicantPhone).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.ApplicantEmail).HasColumnType("nvarchar(256)");

        builder.Property(r => r.DeclarationAcceptedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.DeclarationIp).HasColumnType("varchar(45)");
        builder.Property(r => r.RiskFlags).HasColumnType("nvarchar(500)");

        builder.Property(r => r.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(r => r.SubmittedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.SlaDueAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.DecidedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.DecisionNote).HasColumnType("nvarchar(1000)");
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.UpdatedAt).HasColumnType("datetime2(3)");

        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasMany(r => r.Documents).WithOne().HasForeignKey(d => d.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.OfficeBearers).WithOne().HasForeignKey(b => b.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.ChecklistEntries).WithOne().HasForeignKey(e => e.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Clarifications).WithOne().HasForeignKey(c => c.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.StatusHistory).WithOne().HasForeignKey(h => h.RequestId).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(r => r.IsEditableByApplicant);
        builder.Ignore(r => r.LiveDocuments);
        builder.Ignore(r => r.OpenQuestions);
    }
}

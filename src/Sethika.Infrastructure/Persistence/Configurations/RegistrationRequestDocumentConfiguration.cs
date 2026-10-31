using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;
using Sethika.Domain.Onboarding;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationRequestDocumentConfiguration : IEntityTypeConfiguration<RegistrationRequestDocument>
{
    public void Configure(EntityTypeBuilder<RegistrationRequestDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistrationRequestDocuments", "org");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ReferenceNo).HasColumnType("varchar(80)");
        builder.Property(d => d.IssuedBy).HasColumnType("nvarchar(200)");
        builder.Property(d => d.IssuedDate).HasColumnType("date");
        builder.Property(d => d.ExpiryDate).HasColumnType("date");
        builder.Property(d => d.VerificationStatus).HasColumnType("varchar(20)").IsRequired();
        builder.Property(d => d.VerifiedAt).HasColumnType("datetime2(3)");
        builder.Property(d => d.ReviewerRemarks).HasColumnType("nvarchar(500)");
        builder.Property(d => d.UploadedAt).HasColumnType("datetime2(3)");

        // UX_ReqDoc_Live. A replacement's "superseded" UPDATE must reach the table before the new
        // upload's INSERT, or the index refuses the insert. EF Core 10 already orders them that way
        // without this (removing it survived ReplacingADocumentSupersedesTheOldOneInTheSameSave);
        // declaring it means the order rests on EF knowing the index, not on its default ordering.
        builder.HasIndex(d => new { d.RequestId, d.DocumentTypeId })
            .IsUnique()
            .HasDatabaseName("UX_ReqDoc_Live")
            .HasFilter("[VerificationStatus] <> 'Superseded'");

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(d => d.IsLive);
    }
}

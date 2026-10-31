using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Core;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Files", "core");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.StorageProvider).HasColumnType("varchar(20)").IsRequired();
        builder.Property(f => f.StorageKey).HasColumnType("nvarchar(400)").IsRequired();
        builder.Property(f => f.OriginalName).HasColumnType("nvarchar(255)").IsRequired();
        builder.Property(f => f.ContentType).HasColumnType("varchar(100)").IsRequired();
        builder.Property(f => f.Sha256Hash).HasColumnType("char(64)").IsRequired();
        builder.Property(f => f.ScanStatus).HasColumnType("varchar(15)").IsRequired();
        builder.Property(f => f.UploadedAt).HasColumnType("datetime2(3)");
    }
}

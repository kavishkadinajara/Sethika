using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sethika.Domain.Access;

namespace Sethika.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users", "auth");
        builder.HasKey(u => u.Id);

        // NEWSEQUENTIALID() default lives in the schema
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        builder.Property(u => u.UserType).HasColumnType("varchar(20)").IsRequired();
        builder.Property(u => u.FullName).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(u => u.DisplayName).HasColumnType("nvarchar(80)");
        builder.Property(u => u.Email).HasColumnType("nvarchar(256)");
        builder.Property(u => u.NormalizedEmail).HasColumnType("nvarchar(256)");
        builder.Property(u => u.PhoneNumber).HasColumnType("varchar(20)");
        builder.Property(u => u.NicNumber).HasColumnType("varchar(12)");
        builder.Property(u => u.PasswordHash).HasColumnType("nvarchar(max)");
        builder.Property(u => u.SecurityStamp).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(u => u.PreferredLanguage).HasColumnType("char(2)").IsRequired();
        builder.Property(u => u.Status).HasColumnType("varchar(20)").IsRequired();

        builder.Property(u => u.PasswordChangedAt).HasColumnType("datetime2(3)");
        builder.Property(u => u.LastLoginAt).HasColumnType("datetime2(3)");
        builder.Property(u => u.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(u => u.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(u => u.DeletedAt).HasColumnType("datetime2(3)");

        builder.Property(u => u.RowVersion).IsRowVersion();
    }
}

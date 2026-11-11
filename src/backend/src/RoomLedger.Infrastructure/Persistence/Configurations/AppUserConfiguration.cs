using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng app_user (id = Cognito sub).
 *
 * @author Tuấn
 * @version 1.0
 */
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Email).HasMaxLength(254).IsRequired();
        b.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        b.HasIndex(e => e.Email).HasDatabaseName("ix_app_user_email");
    }
}

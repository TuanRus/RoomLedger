using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng invite (chỉ lưu hash của token).
 *
 * @author Tuấn
 * @version 1.0
 */
public class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.TokenHash).HasMaxLength(44).IsFixedLength().IsRequired();
        b.Property(e => e.Email).HasMaxLength(254).IsRequired();
        b.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("ux_invite_token_hash");
        b.HasIndex(e => e.TenancyId).HasDatabaseName("ix_invite_tenancy_id");
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
    }
}

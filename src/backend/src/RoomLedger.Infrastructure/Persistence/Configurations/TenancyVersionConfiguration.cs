using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng tenancy_version.
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyVersionConfiguration : IEntityTypeConfiguration<TenancyVersion>
{
    public void Configure(EntityTypeBuilder<TenancyVersion> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.AiTermsJson).HasColumnType("jsonb");
        b.Property(e => e.EditedFields).HasColumnType("text[]");
        b.Property(e => e.RejectReason).HasMaxLength(500);
        b.HasIndex(e => new { e.TenancyId, e.Number }).IsUnique().HasDatabaseName("ux_tenancy_version_tenancy_id_number");
        b.HasMany(e => e.Files).WithOne().HasForeignKey(f => f.TenancyVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

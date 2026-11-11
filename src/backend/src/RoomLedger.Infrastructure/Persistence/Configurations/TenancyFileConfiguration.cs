using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng tenancy_file.
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyFileConfiguration : IEntityTypeConfiguration<TenancyFile>
{
    public void Configure(EntityTypeBuilder<TenancyFile> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        b.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        b.Property(e => e.Sha256).HasMaxLength(44).IsFixedLength().IsRequired();
        b.Property(e => e.IncomingKey).HasMaxLength(500).IsRequired();
        b.Property(e => e.EvidenceKey).HasMaxLength(500);
        b.Property(e => e.ObjectVersionId).HasMaxLength(200);
        b.HasIndex(e => e.TenancyVersionId).HasDatabaseName("ix_tenancy_file_tenancy_version_id");
    }
}

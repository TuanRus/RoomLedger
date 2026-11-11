using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng photo.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        b.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        b.Property(e => e.Sha256).HasMaxLength(44).IsFixedLength().IsRequired();
        b.Property(e => e.IncomingKey).HasMaxLength(500).IsRequired();
        b.Property(e => e.DerivedKey).HasMaxLength(500);
        b.Property(e => e.EvidenceKey).HasMaxLength(500);
        b.Property(e => e.ObjectVersionId).HasMaxLength(200);
        b.HasIndex(e => e.InspectionId).HasDatabaseName("ix_photo_inspection_id");
        b.HasIndex(e => e.RoomAreaId).HasDatabaseName("ix_photo_room_area_id");
        b.HasOne<Inspection>().WithMany().HasForeignKey(e => e.InspectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RoomArea>().WithMany().HasForeignKey(e => e.RoomAreaId).OnDelete(DeleteBehavior.Restrict);
    }
}

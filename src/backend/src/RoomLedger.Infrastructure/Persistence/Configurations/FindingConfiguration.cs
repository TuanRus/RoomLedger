using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Bảng finding (MD-06). Số tiền &gt; 0 khi có; độ tin cậy 0–1; ảnh tham chiếu bảng photo.
 *
 * @author Sang
 * @version 1.0
 */
public class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Title).HasMaxLength(80).IsRequired();
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.BoundingBoxJson).HasColumnType("jsonb");
        b.Property(e => e.Confidence).HasPrecision(4, 3);
        b.Property(e => e.ModelId).HasMaxLength(200);
        b.HasIndex(e => e.InspectionId).HasDatabaseName("ix_finding_inspection_id");
        b.HasOne(e => e.Inspection).WithMany().HasForeignKey(e => e.InspectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RoomArea>().WithMany().HasForeignKey(e => e.RoomAreaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Photo>().WithMany().HasForeignKey(e => e.MoveInPhotoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Photo>().WithMany().HasForeignKey(e => e.MoveOutPhotoId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_finding_amount_positive", "amount IS NULL OR amount > 0");
            t.HasCheckConstraint("ck_finding_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
        });
    }
}

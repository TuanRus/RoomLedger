using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng meter_reading. Unique (room_id, period, kind).
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Period).HasMaxLength(7).IsFixedLength().IsRequired();
        b.Property(e => e.AiConfidence).HasPrecision(4, 3);
        b.Property(e => e.AiRawJson).HasColumnType("jsonb");
        b.Property(e => e.ReviewReason).HasMaxLength(40);
        b.Property(e => e.Note).HasMaxLength(500);
        b.Property(e => e.PhotoFileName).HasMaxLength(255);
        b.Property(e => e.PhotoContentType).HasMaxLength(100);
        b.Property(e => e.PhotoSha256).HasMaxLength(44).IsFixedLength();
        b.Property(e => e.PhotoKey).HasMaxLength(500);
        b.HasIndex(e => new { e.RoomId, e.Period, e.Kind }).IsUnique().HasDatabaseName("ux_meter_reading_room_id_period_kind");
        b.HasIndex(e => e.TenancyId).HasDatabaseName("ix_meter_reading_tenancy_id");
        b.HasOne<Room>().WithMany().HasForeignKey(e => e.RoomId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
    }
}

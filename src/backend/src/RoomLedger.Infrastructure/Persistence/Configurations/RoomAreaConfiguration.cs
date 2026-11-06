using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng room_area.
 *
 * @author Tuấn
 * @version 1.0
 */
public class RoomAreaConfiguration : IEntityTypeConfiguration<RoomArea>
{
    public void Configure(EntityTypeBuilder<RoomArea> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(e => e.RoomId).HasDatabaseName("ix_room_area_room_id");
    }
}

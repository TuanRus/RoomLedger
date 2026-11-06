using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng room. Version là cột hệ thống xmin của PostgreSQL (concurrency token).
 *
 * @author Tuấn
 * @version 1.0
 */
public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Code).HasMaxLength(20).IsRequired();
        // Npgsql map uint + IsRowVersion vào cột hệ thống xmin (cách thay cho UseXminAsConcurrencyToken đã obsolete)
        b.Property(e => e.Version).IsRowVersion().HasColumnName("xmin");
        b.HasIndex(e => new { e.PropertyId, e.Code }).IsUnique().HasDatabaseName("ux_room_property_id_code");
        b.HasIndex(e => new { e.PropertyId, e.Status }).HasDatabaseName("ix_room_property_id_status");
        b.HasIndex(e => new { e.LandlordId, e.CreatedAt, e.Id }).HasDatabaseName("ix_room_landlord_id_created_at");
        b.HasMany(e => e.Areas).WithOne().HasForeignKey(a => a.RoomId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.Tenancies).WithOne(t => t.Room).HasForeignKey(t => t.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

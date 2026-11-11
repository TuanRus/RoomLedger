using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng property.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Name).HasMaxLength(100).IsRequired();
        b.Property(e => e.Address).HasMaxLength(255).IsRequired();
        b.HasIndex(e => new { e.LandlordId, e.CreatedAt, e.Id }).HasDatabaseName("ix_property_landlord_id_created_at");
        b.HasMany(e => e.Rooms).WithOne(r => r.Property).HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Restrict);
    }
}

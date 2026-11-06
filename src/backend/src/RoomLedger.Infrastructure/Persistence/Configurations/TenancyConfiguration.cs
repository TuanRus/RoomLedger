using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng tenancy. Mỗi phòng chỉ có một hợp đồng chưa quyết toán (unique index có điều kiện).
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyConfiguration : IEntityTypeConfiguration<Tenancy>
{
    public void Configure(EntityTypeBuilder<Tenancy> b)
    {
        b.ConfigureAuditable();
        b.HasIndex(e => e.RoomId).IsUnique().HasFilter("status <> 'SETTLED'").HasDatabaseName("ux_tenancy_room_id_open");
        b.HasIndex(e => e.LandlordId).HasDatabaseName("ix_tenancy_landlord_id");
        b.HasIndex(e => e.TenantId).HasDatabaseName("ix_tenancy_tenant_id");
        b.HasMany(e => e.Versions).WithOne(v => v.Tenancy).HasForeignKey(v => v.TenancyId).OnDelete(DeleteBehavior.Restrict);
    }
}

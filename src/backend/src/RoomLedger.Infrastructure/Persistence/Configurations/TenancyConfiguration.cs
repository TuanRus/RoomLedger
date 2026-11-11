using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng tenancy. Mỗi phòng chỉ có một hợp đồng chưa quyết toán (unique index có điều kiện).
 * Chỉ số ban đầu có check constraint không âm để database tự chặn dữ liệu sai, kể cả khi ghi từ Lambda khác API.
 *
 * @author Tuấn
 * @version 1.1
 */
public class TenancyConfiguration : IEntityTypeConfiguration<Tenancy>
{
    public void Configure(EntityTypeBuilder<Tenancy> b)
    {
        b.ConfigureAuditable();
        b.HasIndex(e => e.RoomId).IsUnique().HasFilter("status <> 'SETTLED'").HasDatabaseName("ux_tenancy_room_id_open");
        b.HasIndex(e => e.LandlordId).HasDatabaseName("ix_tenancy_landlord_id");
        b.HasIndex(e => e.TenantId).HasDatabaseName("ix_tenancy_tenant_id");
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_tenancy_opening_electricity", "opening_electricity IS NULL OR opening_electricity >= 0");
            t.HasCheckConstraint("ck_tenancy_opening_water", "opening_water IS NULL OR opening_water >= 0");
        });
        b.HasMany(e => e.Versions).WithOne(v => v.Tenancy).HasForeignKey(v => v.TenancyId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng inspection. Một biên bản mỗi loại cho mỗi hợp đồng.
 *
 * @author Tuấn
 * @version 1.0
 */
public class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> b)
    {
        b.ConfigureAuditable();
        b.HasIndex(e => new { e.TenancyId, e.Type }).IsUnique().HasDatabaseName("ux_inspection_tenancy_id_type");
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
    }
}

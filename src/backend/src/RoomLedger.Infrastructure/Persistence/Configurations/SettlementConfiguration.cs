using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng settlement (MD-06-FE-05, BR-29). Unique tenancy_id: hai lần quyết toán cùng lúc chỉ ghi được một dòng.
 *
 * @author Sang
 * @version 1.0
 */
public class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> b)
    {
        b.ConfigureAuditable();
        b.HasIndex(e => e.TenancyId).IsUnique().HasDatabaseName("ux_settlement_tenancy_id");
        b.Property(e => e.PdfKey).HasMaxLength(300);
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Inspection>().WithMany().HasForeignKey(e => e.InspectionId).OnDelete(DeleteBehavior.Restrict);
        // settled_by: id người dùng (sub Cognito), không khóa ngoại tới app_user, giống dispute.raised_by và tenancy.landlord_id
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_settlement_amounts", "deposit >= 0 AND deductions_total >= 0 AND unpaid_invoices_total >= 0");
            t.HasCheckConstraint("ck_settlement_refund",
                "refund = deposit - deductions_total - unpaid_invoices_total");
        });
    }
}

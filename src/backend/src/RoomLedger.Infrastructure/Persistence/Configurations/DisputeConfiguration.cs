using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng dispute.
 *
 * @author Tuấn
 * @version 1.0
 */
public class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Reason).HasMaxLength(1000).IsRequired();
        b.Property(e => e.Response).HasMaxLength(1000);
        b.HasIndex(e => e.TenancyId).HasDatabaseName("ix_dispute_tenancy_id");
        b.HasIndex(e => e.InvoiceId).HasDatabaseName("ix_dispute_invoice_id");
        b.HasIndex(e => new { e.TargetType, e.TargetId }).HasDatabaseName("ix_dispute_target_type_target_id");
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Invoice>().WithMany().HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

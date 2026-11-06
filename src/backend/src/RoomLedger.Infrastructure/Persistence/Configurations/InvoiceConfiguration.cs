using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng invoice. Unique (tenancy_id, period): một hóa đơn mỗi kỳ.
 *
 * @author Tuấn
 * @version 1.0
 */
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ConfigureAuditable();
        b.Property(e => e.Period).HasMaxLength(7).IsFixedLength().IsRequired();
        b.HasIndex(e => new { e.TenancyId, e.Period }).IsUnique().HasDatabaseName("ux_invoice_tenancy_id_period");
        b.HasIndex(e => new { e.Period, e.Status }).HasDatabaseName("ix_invoice_period_status");
        b.HasIndex(e => e.ElectricityReadingId).HasDatabaseName("ix_invoice_electricity_reading_id");
        b.HasIndex(e => e.WaterReadingId).HasDatabaseName("ix_invoice_water_reading_id");
        b.HasOne(e => e.Tenancy).WithMany().HasForeignKey(e => e.TenancyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeterReading>().WithMany().HasForeignKey(e => e.ElectricityReadingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeterReading>().WithMany().HasForeignKey(e => e.WaterReadingId).OnDelete(DeleteBehavior.Restrict);
    }
}

using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng invoice: hóa đơn tháng của một hợp đồng. Unique (tenancy_id, period).
 * Mọi số tiền là bigint VND; giữ lại chỉ số và đơn giá đã dùng để tra lại được cách tính.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Invoice : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public string Period { get; set; } = "";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public long RoomRent { get; set; }
    public Guid ElectricityReadingId { get; set; }
    public long ElectricityPrevious { get; set; }
    public long ElectricityCurrent { get; set; }
    public long ElectricityRate { get; set; }
    public long ElectricityAmount { get; set; }
    public Guid WaterReadingId { get; set; }
    public long WaterPrevious { get; set; }
    public long WaterCurrent { get; set; }
    public long WaterRate { get; set; }
    public long WaterAmount { get; set; }
    public long ServiceFee { get; set; }
    public long Total { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Tenancy? Tenancy { get; set; }
}

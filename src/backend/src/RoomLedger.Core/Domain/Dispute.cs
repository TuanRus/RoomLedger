using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng dispute: khiếu nại của người thuê về chỉ số (MD-05) hoặc mục bị trừ cọc (MD-06).
 *
 * @author Tuấn
 * @version 1.1
 */
public class Dispute : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public DisputeTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid RaisedBy { get; set; }
    public string Reason { get; set; } = "";
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public DisputeResolution? Resolution { get; set; }
    public string? Response { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Tenancy? Tenancy { get; set; }
}

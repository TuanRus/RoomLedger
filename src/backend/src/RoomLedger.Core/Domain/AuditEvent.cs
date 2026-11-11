namespace RoomLedger.Core.Domain;

/**
 * Bảng audit_event: nhật ký kiểm toán chỉ ghi thêm (roomledger_app chỉ có INSERT, SELECT).
 * Không có updated_at/updated_by vì bản ghi không bao giờ bị sửa. Không ghi email hay số điện thoại.
 *
 * @author Tuấn
 * @version 1.1
 */
public class AuditEvent
{
    public Guid Id { get; set; } = Common.UuidV7.NewGuid();
    public string EventType { get; set; } = "";
    public Guid? ActorId { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public string? DetailJson { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

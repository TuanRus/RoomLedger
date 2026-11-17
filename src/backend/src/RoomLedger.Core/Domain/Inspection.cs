using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng inspection: biên bản nhận hoặc trả phòng. Đây là phần tối thiểu để POST /uploads
 * và trạng thái hiển thị của phòng dùng được; nghiệp vụ MD-04, MD-06 do Sang bổ sung.
 *
 * @author Tuấn
 * @version 1.2
 */
public class Inspection : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public InspectionType Type { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Open;
    public DateTimeOffset? LandlordConfirmedAt { get; set; }
    public DateTimeOffset? TenantConfirmedAt { get; set; }
    /** Bản sao khu vực lúc mở (jsonb, danh sách InspectionArea); đọc/ghi qua InspectionService (Sang, BR-15 R2). */
    public string? AreaSnapshotJson { get; set; }
    /** Kỳ trả phòng yyyy-MM (chỉ MOVE_OUT), chốt lúc mở theo giờ Việt Nam (Sang, quyết định A3 của Tuấn 17/11). */
    public string? FinalPeriod { get; set; }
    public Tenancy? Tenancy { get; set; }
}

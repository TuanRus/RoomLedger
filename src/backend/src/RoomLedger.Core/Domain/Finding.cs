using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng finding: một mục so sánh của biên bản trả phòng (BR-26 – BR-28), do AI tạo (persist-results) hoặc chủ nhà
 * thêm tay (BR-27 RULE 3, IsManual). Amount (VND) chỉ có khi CHARGED trở đi. BoundingBoxJson là jsonb
 * {x, y, width, height} tỉ lệ 0–1 trên ảnh trả phòng. Thiết kế đề xuất của Sang, chờ Tuấn duyệt ERD.
 *
 * @author Sang
 * @version 1.0
 */
public class Finding : AuditableEntity
{
    public Guid InspectionId { get; set; }
    public Guid RoomAreaId { get; set; }
    public Guid? MoveInPhotoId { get; set; }
    public Guid? MoveOutPhotoId { get; set; }
    public int? PhashDistance { get; set; }
    public FindingStatus Status { get; set; } = FindingStatus.Suggested;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? BoundingBoxJson { get; set; }
    public decimal? Confidence { get; set; }
    public string? ModelId { get; set; }
    public bool UsedFallback { get; set; }
    public bool IsManual { get; set; }
    public long? Amount { get; set; }
    public Inspection? Inspection { get; set; }
}

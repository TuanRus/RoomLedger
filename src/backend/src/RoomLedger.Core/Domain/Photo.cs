using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng photo: ảnh hiện trạng của một khu vực trong biên bản. Tạo lúc xin presigned URL
 * (PENDING_UPLOAD); photo-ingest, photo-derive, evidence-seal (Sang) cập nhật các cột còn lại.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Photo : AuditableEntity
{
    public Guid InspectionId { get; set; }
    public Guid RoomAreaId { get; set; }
    public Guid UploadedBy { get; set; }
    public UserRole UploaderRole { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public FileStatus Status { get; set; } = FileStatus.PendingUpload;
    public string IncomingKey { get; set; } = "";
    public string? DerivedKey { get; set; }
    public string? EvidenceKey { get; set; }
    public string? ObjectVersionId { get; set; }
    public DateTimeOffset? ExifTakenAt { get; set; }
    public bool OldPhotoWarning { get; set; }
}

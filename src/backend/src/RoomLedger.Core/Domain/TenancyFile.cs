using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng tenancy_file: một file scan của phiên bản hợp đồng. Tạo lúc xin presigned URL
 * (PENDING_UPLOAD); photo-ingest đổi sang RECEIVED, evidence-seal ghi EvidenceKey và ObjectVersionId.
 *
 * @author Tuấn
 * @version 1.1
 */
public class TenancyFile : AuditableEntity
{
    public Guid TenancyVersionId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public FileStatus Status { get; set; } = FileStatus.PendingUpload;
    public string IncomingKey { get; set; } = "";
    public string? EvidenceKey { get; set; }
    public string? ObjectVersionId { get; set; }
}

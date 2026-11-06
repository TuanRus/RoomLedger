using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng meter_reading: chỉ số điện hoặc nước của một phòng trong một kỳ, kèm ảnh đồng hồ.
 * Unique (room_id, period, kind). AiValue do Lambda persist-results ghi; Value là số đã xác nhận.
 *
 * @author Tuấn
 * @version 1.1
 */
public class MeterReading : AuditableEntity
{
    public Guid RoomId { get; set; }
    public Guid TenancyId { get; set; }
    public string Period { get; set; } = "";
    public MeterKind Kind { get; set; }
    public MeterReadingStatus Status { get; set; } = MeterReadingStatus.PendingAi;
    public long? AiValue { get; set; }
    public decimal? AiConfidence { get; set; }
    public string? AiRawJson { get; set; }
    public long? Value { get; set; }
    public string? ReviewReason { get; set; }
    public bool MeterReplaced { get; set; }
    public string? Note { get; set; }
    public bool EditedByLandlord { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? PhotoFileName { get; set; }
    public string? PhotoContentType { get; set; }
    public long? PhotoSize { get; set; }
    public string? PhotoSha256 { get; set; }
    public FileStatus? PhotoStatus { get; set; }
    public string? PhotoKey { get; set; }
    public Tenancy? Tenancy { get; set; }
}

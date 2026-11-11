using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body POST /rooms/{roomId}/meter-readings: tạo chỉ số kỳ và xin URL tải ảnh đồng hồ (MD-05-FE-01).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateMeterReadingRequest(
    MeterKind? Kind,
    string? Period,
    string? FileName,
    string? ContentType,
    long? Size,
    string? Sha256);

/**
 * Body PATCH /meter-readings/{readingId}: sửa và/hoặc xác nhận chỉ số (MD-05-FE-02).
 *
 * @author Tuấn
 * @version 1.0
 */
public record UpdateMeterReadingRequest(long? Value, bool? MeterReplaced, string? Note, bool? Confirm);

/**
 * Body POST /properties/{propertyId}/meter-readings:confirm-all.
 *
 * @author Tuấn
 * @version 1.0
 */
public record ConfirmAllReadingsRequest(string? Period);

/**
 * Một chỉ số điện hoặc nước.
 *
 * @author Tuấn
 * @version 1.0
 */
public record MeterReadingResponse(
    Guid ReadingId,
    Guid RoomId,
    MeterKind Kind,
    string Period,
    MeterReadingStatus Status,
    long? Value,
    long? AiValue,
    decimal? AiConfidence,
    long? PreviousValue,
    bool NeedsReview,
    string? ReviewReason,
    bool MeterReplaced,
    string? Note,
    bool EditedByLandlord,
    DateTimeOffset? ConfirmedAt,
    FileStatus? PhotoStatus,
    string? PhotoUrl);

/**
 * Chỉ số vừa tạo kèm URL tải ảnh đồng hồ.
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateMeterReadingResponse(MeterReadingResponse Reading, UploadUrlResponse Upload);

/**
 * Một dòng trong bảng chỉ số của kỳ: phòng và hai chỉ số điện, nước.
 *
 * @author Tuấn
 * @version 1.0
 */
public record RoomReadingsResponse(Guid RoomId, string RoomCode, Guid TenancyId, MeterReadingResponse? Electricity, MeterReadingResponse? Water);

/**
 * Kết quả "Xác nhận tất cả": số chỉ số đã xác nhận và các chỉ số bị bỏ qua.
 *
 * @author Tuấn
 * @version 1.0
 */
public record ConfirmAllReadingsResponse(int Confirmed, IReadOnlyList<Guid> Skipped);

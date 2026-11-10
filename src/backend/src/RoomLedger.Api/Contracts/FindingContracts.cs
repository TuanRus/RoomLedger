using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/** Khung trên ảnh trả phòng, tỉ lệ 0–1. */
public record BoundingBoxResponse(double X, double Y, double Width, double Height);

/**
 * Một mục so sánh trả phòng cho FE (MD-06-FE-03/04). Amount (VND) có khi đã tính tiền.
 *
 * @author Sang
 * @version 1.0
 */
public record FindingResponse(
    Guid FindingId,
    Guid AreaId,
    Guid? MoveInPhotoId,
    Guid? MoveOutPhotoId,
    FindingStatus Status,
    string Title,
    string? Description,
    BoundingBoxResponse? BoundingBox,
    decimal? Confidence,
    int? PhashDistance,
    bool IsManual,
    bool UsedFallback,
    long? Amount);

/** Tính tiền một mục (BR-27 RULE 1): số nguyên VND lớn hơn 0. */
public record ChargeFindingRequest(long? Amount);

/** Mục tay (BR-27 RULE 3): bắt buộc một cặp ảnh nhận phòng / trả phòng cùng khu vực. */
public record AddFindingRequest(Guid? AreaId, string? Title, string? Description, long? Amount, Guid? MoveInPhotoId, Guid? MoveOutPhotoId);

/** Người thuê khiếu nại một mục (BR-28 RULE 2): lý do từ 10 ký tự. */
public record DisputeFindingRequest(string? Reason);

/** Chủ nhà trả lời khiếu nại (BR-28 RULE 3): Action = CORRECTED (kèm Amount), KEPT (kèm Response) hoặc REMOVED. */
public record RespondFindingDisputeRequest(string? Action, long? Amount, string? Response);

/** Kết quả gửi danh sách cho người thuê (BR-27 RULE 4). */
public record SendFindingsResponse(Guid InspectionId, InspectionStatus Status, long Total);

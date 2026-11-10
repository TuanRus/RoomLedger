using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Một khu vực của biên bản (theo bản sao lúc mở) kèm số ảnh đã gửi.
 *
 * @author Sang
 * @version 1.0
 */
public record InspectionAreaResponse(Guid AreaId, string Name, int MinPhotos, int SortOrder, int PhotoCount);

/**
 * Biên bản nhận/trả phòng cho chủ nhà và người thuê của hợp đồng.
 *
 * @author Sang
 * @version 1.0
 */
public record InspectionResponse(
    Guid InspectionId,
    Guid TenancyId,
    InspectionType Type,
    InspectionStatus Status,
    DateTimeOffset? LandlordConfirmedAt,
    DateTimeOffset? TenantConfirmedAt,
    IReadOnlyList<InspectionAreaResponse> Areas,
    Guid? ReferenceInspectionId = null);   // biên bản trả phòng: id biên bản nhận phòng để hiện ảnh đối chiếu

/**
 * Body POST /inspections/{id}/confirm: agreed = người dùng đã tích ô cam kết (BR-18 TRIGGER).
 *
 * @author Sang
 * @version 1.0
 */
public record ConfirmInspectionRequest(bool? Agreed);

/**
 * Một ảnh của biên bản cho FE. URL là presigned GET tới bucket derived (5 phút); không có khóa S3 hay SHA-256.
 * viewUrl/thumbnailUrl null khi ảnh đang xử lý (RECEIVED). canDelete: người gọi là người gửi và biên bản chưa khóa.
 *
 * @author Sang
 * @version 1.0
 */
public record InspectionPhotoResponse(
    Guid PhotoId,
    Guid AreaId,
    string FileName,
    FileStatus Status,
    UserRole UploaderRole,
    bool UploadedByMe,
    CaptureSource CaptureSource,
    DateTimeOffset? ExifTakenAt,
    bool OldPhotoWarning,
    DateTimeOffset UploadedAt,
    string? ViewUrl,
    string? ThumbnailUrl,
    bool CanDelete);

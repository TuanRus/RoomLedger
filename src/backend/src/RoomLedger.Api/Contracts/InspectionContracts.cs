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
    IReadOnlyList<InspectionAreaResponse> Areas);

/**
 * Body POST /inspections/{id}/confirm: agreed = người dùng đã tích ô cam kết (BR-18 TRIGGER).
 *
 * @author Sang
 * @version 1.0
 */
public record ConfirmInspectionRequest(bool? Agreed);

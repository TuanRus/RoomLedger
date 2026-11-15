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
    long? OpeningElectricity = null,   // BR-18 RULE 5: chỉ số ban đầu (chỉ biên bản nhận phòng)
    long? OpeningWater = null);

/**
 * Body POST /inspections/{id}/confirm: agreed = người dùng đã tích ô cam kết (BR-18 TRIGGER).
 *
 * @author Sang
 * @version 1.0
 */
public record ConfirmInspectionRequest(bool? Agreed);

/** Chỉ số điện nước ban đầu (BR-18 RULE 5): số nguyên từ 0, bắt buộc cả hai. */
public record OpeningReadingsRequest(long? Electricity, long? Water);

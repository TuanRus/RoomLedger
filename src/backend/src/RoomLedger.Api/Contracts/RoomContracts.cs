using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Một khu vực trong body tạo hoặc sửa phòng. areaId có khi sửa khu vực đã có.
 *
 * @author Tuấn
 * @version 1.0
 */
public record AreaRequest(Guid? AreaId, string? Name, int MinPhotos);

/**
 * Body POST /properties/{propertyId}/rooms (MD-02-FE-02).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateRoomRequest(string? Code, long? MonthlyRent, List<AreaRequest>? Areas);

/**
 * Body PATCH /rooms/{roomId} (MD-02-FE-03). version là giá trị client đọc được lần trước;
 * areas null nghĩa là không đổi danh sách khu vực.
 *
 * @author Tuấn
 * @version 1.0
 */
public record UpdateRoomRequest(long? MonthlyRent, List<AreaRequest>? Areas, uint? Version);

/**
 * Khu vực cần chụp.
 *
 * @author Tuấn
 * @version 1.0
 */
public record AreaResponse(Guid AreaId, string Name, int MinPhotos);

/**
 * Phòng kèm trạng thái hiển thị và người thuê hiện tại.
 *
 * @author Tuấn
 * @version 1.0
 */
public record RoomResponse(
    Guid RoomId,
    Guid PropertyId,
    string Code,
    long MonthlyRent,
    RoomStatus Status,
    RoomDisplayStatus DisplayStatus,
    string? TenantName,
    IReadOnlyList<AreaResponse> Areas,
    uint Version);

/**
 * Hợp đồng hiện tại của phòng (tóm tắt).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CurrentTenancyResponse(Guid TenancyId, TenancyStatus Status, string? TenantName);

/**
 * Chi tiết phòng: thông tin phòng, khu vực và hợp đồng hiện tại (MD-02-FE-03).
 *
 * @author Tuấn
 * @version 1.0
 */
public record RoomDetailResponse(
    Guid RoomId,
    Guid PropertyId,
    string PropertyName,
    string Code,
    long MonthlyRent,
    RoomStatus Status,
    RoomDisplayStatus DisplayStatus,
    IReadOnlyList<AreaResponse> Areas,
    uint Version,
    CurrentTenancyResponse? CurrentTenancy);

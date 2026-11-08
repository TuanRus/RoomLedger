namespace RoomLedger.Functions.Shared;

/**
 * Phần detail của sự kiện InspectionLocked mà API phát khi biên bản khóa (BR-18 RULE 3).
 * API gửi thêm "type"; Lambda chỉ cần inspectionId.
 *
 * @author Sang
 * @version 1.0
 */
public sealed record InspectionLockedDetail(Guid InspectionId, Guid TenancyId);

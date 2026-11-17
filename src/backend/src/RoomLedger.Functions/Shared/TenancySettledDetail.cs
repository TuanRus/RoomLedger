namespace RoomLedger.Functions.Shared;

/**
 * Phần detail của sự kiện TenancySettled mà API phát khi chủ nhà quyết toán (MD-06-FE-05, BR-29).
 * API gửi thêm inspectionId, refund, notifyUserId; render-pdf chỉ cần hợp đồng và quyết toán.
 *
 * @author Sang
 * @version 1.0
 */
public sealed record TenancySettledDetail(Guid TenancyId, Guid SettlementId);

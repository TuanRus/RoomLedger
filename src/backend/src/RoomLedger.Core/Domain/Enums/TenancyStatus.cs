namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái hợp đồng: DRAFT → AWAITING_TENANT → ACTIVE → MOVING_OUT → SETTLED.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum TenancyStatus
{
    Draft,
    AwaitingTenant,
    Active,
    MovingOut,
    Settled,
}

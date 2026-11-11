namespace RoomLedger.Core.Domain.Enums;

/**
 * Quyết định của người thuê khi xác nhận hợp đồng.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum ConfirmDecision
{
    Confirm,
    Reject,
}

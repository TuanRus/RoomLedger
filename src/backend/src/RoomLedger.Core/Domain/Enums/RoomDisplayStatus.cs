namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái hiển thị của phòng, tính từ hợp đồng và biên bản (BR-08 RULE 2).
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum RoomDisplayStatus
{
    Vacant,
    AwaitingConfirm,
    Active,
    MovingOut,
}

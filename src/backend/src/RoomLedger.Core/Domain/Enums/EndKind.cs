namespace RoomLedger.Core.Domain.Enums;

/**
 * Hình thức kết thúc hợp đồng: LEAVE (người thuê rời đi) hoặc MOVE (chuyển sang phòng khác cùng nhà trọ).
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum EndKind
{
    Leave,
    Move,
}

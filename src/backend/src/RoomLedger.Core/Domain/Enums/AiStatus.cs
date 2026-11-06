namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái xử lý AI của một bản ghi (đọc hợp đồng, đọc đồng hồ).
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum AiStatus
{
    Pending,
    Succeeded,
    Failed,
}

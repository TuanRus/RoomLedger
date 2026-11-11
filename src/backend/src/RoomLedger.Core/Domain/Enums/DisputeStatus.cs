namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái khiếu nại.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum DisputeStatus
{
    Open,
    Resolved,
}

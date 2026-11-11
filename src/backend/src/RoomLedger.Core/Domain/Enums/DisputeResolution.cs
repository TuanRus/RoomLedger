namespace RoomLedger.Core.Domain.Enums;

/**
 * Cách chủ nhà xử lý khiếu nại chỉ số.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum DisputeResolution
{
    Corrected,
    Kept,
}

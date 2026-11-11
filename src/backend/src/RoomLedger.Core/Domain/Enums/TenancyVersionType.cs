namespace RoomLedger.Core.Domain.Enums;

/**
 * Loại phiên bản hợp đồng.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum TenancyVersionType
{
    Initial,
    Addendum,
    Renewal,
}

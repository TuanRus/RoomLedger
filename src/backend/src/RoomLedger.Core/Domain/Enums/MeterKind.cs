namespace RoomLedger.Core.Domain.Enums;

/**
 * Loại đồng hồ.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum MeterKind
{
    Electricity,
    Water,
}

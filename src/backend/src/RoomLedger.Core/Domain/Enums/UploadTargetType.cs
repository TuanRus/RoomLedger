namespace RoomLedger.Core.Domain.Enums;

/**
 * Đối tượng mà file tải lên gắn vào (POST /uploads).
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum UploadTargetType
{
    Inspection,
    TenancyVersion,
    MeterReading,
}

namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái file tải lên: PENDING_UPLOAD → RECEIVED → READY → SEALED.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum FileStatus
{
    PendingUpload,
    Received,
    Ready,
    Sealed,
}

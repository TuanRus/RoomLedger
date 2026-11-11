namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái chỉ số: PENDING_AI → NEEDS_REVIEW hoặc PENDING_CONFIRM → CONFIRMED.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum MeterReadingStatus
{
    PendingAi,
    NeedsReview,
    PendingConfirm,
    Confirmed,
}

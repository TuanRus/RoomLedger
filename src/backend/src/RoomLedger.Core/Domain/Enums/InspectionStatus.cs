namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái biên bản. MOVE_IN: OPEN → AWAITING_CONFIRM → LOCKED. MOVE_OUT: OPEN → REVIEWING → SETTLED.
 * COMPARING không còn dùng (đã bỏ AI so ảnh, kiến trúc v8.5.2); giữ giá trị để không đổi enum của API và FE.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum InspectionStatus
{
    Open,
    AwaitingConfirm,
    Locked,
    Comparing,
    Reviewing,
    Settled,
}

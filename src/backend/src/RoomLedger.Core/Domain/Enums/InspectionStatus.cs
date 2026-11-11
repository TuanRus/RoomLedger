namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái biên bản. MOVE_IN: OPEN → AWAITING_CONFIRM → LOCKED. MOVE_OUT: OPEN → COMPARING → REVIEWING → SETTLED.
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

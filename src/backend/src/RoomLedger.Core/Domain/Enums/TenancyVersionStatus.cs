namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái phiên bản hợp đồng: UPLOADED → TERMS_REVIEWED → AWAITING_CONFIRM → LOCKED, hoặc REJECTED.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum TenancyVersionStatus
{
    Uploaded,
    TermsReviewed,
    AwaitingConfirm,
    Locked,
    Rejected,
}

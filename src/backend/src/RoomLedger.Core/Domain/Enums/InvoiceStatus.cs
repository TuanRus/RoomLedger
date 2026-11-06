namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái hóa đơn: DRAFT → SENT → PAID; SENT ↔ DISPUTED.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum InvoiceStatus
{
    Draft,
    Sent,
    Paid,
    Disputed,
}

namespace RoomLedger.Core.Domain.Enums;

/**
 * Trạng thái một mục so sánh trả phòng (BR-26 – BR-28).
 * AI: SUGGESTED (có thể hư hỏng), SKIPPED (cặp gần giống, không gọi AI), NOT_ANALYZED (AI lỗi hoặc vượt giới hạn).
 * Chủ nhà: CHARGED (tính tiền), DISMISSED (bỏ qua). Người thuê: ACCEPTED, DISPUTED; sau xử lý khiếu nại: RESOLVED.
 *
 * @author Sang
 * @version 1.0
 */
public enum FindingStatus
{
    Suggested,
    Skipped,
    NotAnalyzed,
    Charged,
    Dismissed,
    Accepted,
    Disputed,
    Resolved,
}

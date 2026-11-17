using RoomLedger.Core.Services;

namespace RoomLedger.Api.Contracts;

/**
 * Quyết toán trả phòng (MD-06-FE-05, BR-29). Status PENDING (xem trước, số tính lại mỗi lần) hoặc SETTLED (số đã chốt
 * trong bảng settlement). Refund âm: người thuê còn nợ. Blockers: các điều kiện chưa đạt theo thứ tự kiểm.
 * PdfUrl: link tải biên bản thanh lý (5 phút), chỉ có khi đã tạo xong.
 *
 * @author Sang
 * @version 1.0
 */
public record SettlementResponse(
    Guid TenancyId,
    Guid InspectionId,
    string FinalPeriod,
    string Status,
    long Deposit,
    long DeductionsTotal,
    long UnpaidInvoicesTotal,
    long Refund,
    bool CanSettle,
    IReadOnlyList<SettlementBlocker> Blockers,
    Guid? SettlementId = null,
    DateTimeOffset? SettledAt = null,
    Guid? SettledBy = null,
    string? PdfUrl = null)
{
    public const string Pending = "PENDING";
    public const string Settled = "SETTLED";
}

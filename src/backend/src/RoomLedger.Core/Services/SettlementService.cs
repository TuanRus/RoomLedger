using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Điều kiện quyết toán đã đọc từ database (MD-06-FE-05, BR-29).
 *
 * @author Sang
 * @version 1.0
 */
public sealed record SettlementCheck(
    string FinalPeriod,
    IReadOnlyList<MeterKind> UnconfirmedReadings,
    bool FinalInvoiceExists,
    int DisputedFindings,
    int DisputedInvoices,
    int PendingFindings);

/** Một điều kiện quyết toán chưa đạt; trả trong details của lỗi 409 và trong bản xem trước. */
public sealed record SettlementBlocker(string Code, string Message);

/**
 * Quy tắc thuần của quyết toán trả phòng (MD-06-FE-05, BR-29), không đụng database.
 *
 * @author Sang
 * @version 1.0
 */
public static class SettlementService
{
    /**
     * Tiền trả lại người thuê = cọc − tiền trừ (mục ACCEPTED/RESOLVED) − mọi hóa đơn chưa PAID (gồm hóa đơn cuối,
     * đã có điện nước tháng cuối nên không trừ riêng lần nữa). Âm: người thuê còn nợ chủ nhà.
     *
     * @author Sang
     * @version 1.0
     */
    public static long Refund(long deposit, long deductions, long unpaidInvoices) => deposit - deductions - unpaidInvoices;

    /**
     * Các điều kiện quyết toán chưa đạt theo thứ tự đã thống nhất (quyết định B5): chỉ số tháng cuối chưa xác nhận
     * (B3) → chưa có hóa đơn cuối → còn khiếu nại mục trừ cọc hoặc hóa đơn (B4) → người thuê chưa trả lời mục bị tính
     * tiền. Rỗng = quyết toán được.
     *
     * @author Sang
     * @version 1.0
     */
    public static IReadOnlyList<SettlementBlocker> Blockers(SettlementCheck check)
    {
        var blockers = new List<SettlementBlocker>();
        if (check.UnconfirmedReadings.Count > 0)
        {
            var meters = string.Join(", ", check.UnconfirmedReadings.Select(InspectionService.MeterName));
            blockers.Add(new SettlementBlocker(ErrorCodes.FinalReadingUnconfirmed,
                $"Chưa xác nhận chỉ số tháng cuối: {meters} (kỳ {check.FinalPeriod})"));
        }
        if (!check.FinalInvoiceExists)
        {
            blockers.Add(new SettlementBlocker(ErrorCodes.FinalInvoiceMissing, $"Chưa có hóa đơn kỳ {check.FinalPeriod}"));
        }
        if (check.DisputedFindings + check.DisputedInvoices > 0)
        {
            var parts = new List<string>();
            if (check.DisputedFindings > 0)
            {
                parts.Add($"{check.DisputedFindings} mục trừ cọc");
            }
            if (check.DisputedInvoices > 0)
            {
                parts.Add($"{check.DisputedInvoices} hóa đơn");
            }
            blockers.Add(new SettlementBlocker(ErrorCodes.OpenDisputes, "Còn khiếu nại chưa xử lý: " + string.Join(", ", parts)));
        }
        if (check.PendingFindings > 0)
        {
            blockers.Add(new SettlementBlocker(ErrorCodes.PendingFindings, $"Người thuê chưa trả lời {check.PendingFindings} mục trừ cọc"));
        }
        return blockers;
    }
}

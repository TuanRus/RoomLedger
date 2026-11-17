using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Functions.RenderPdf;

/**
 * Nội dung biên bản thanh lý hợp đồng (MD-06-FE-05, BR-29), đã đọc xong từ database; SettlementDocument dựng PDF từ đây.
 * Số tiền là số đã chốt trong bảng settlement. Deductions: mục ACCEPTED (Resolved = false) và RESOLVED (true).
 *
 * @author Sang
 * @version 1.0
 */
public sealed record SettlementReport(
    Guid SettlementId,
    string PropertyName,
    string PropertyAddress,
    string RoomCode,
    string LandlordName,
    string TenantName,
    string FinalPeriod,
    DateTimeOffset SettledAt,
    long Deposit,
    long DeductionsTotal,
    long UnpaidInvoicesTotal,
    long Refund,
    IReadOnlyList<DeductionLine> Deductions,
    IReadOnlyList<InvoiceLine> UnpaidInvoices);

/** Một khoản trừ cọc: khu vực, tiêu đề, số tiền; Resolved = qua khiếu nại rồi mới đồng ý. */
public sealed record DeductionLine(string Area, string Title, long Amount, bool Resolved);

/** Một hóa đơn chưa thanh toán. */
public sealed record InvoiceLine(string Period, InvoiceStatus Status, long Total);

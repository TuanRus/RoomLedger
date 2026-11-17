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
    public static long Refund(long deposit, long deductions, long unpaidInvoices) => throw new NotImplementedException();

    public static IReadOnlyList<SettlementBlocker> Blockers(SettlementCheck check) => throw new NotImplementedException();
}

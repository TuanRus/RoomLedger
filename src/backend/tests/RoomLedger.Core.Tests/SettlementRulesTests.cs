using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Quyết toán (MD-06-FE-05, BR-29; quyết định A2, B3–B5 của Tuấn ngày 17/11).
 * Tiền trả lại = cọc − các mục ACCEPTED/RESOLVED − mọi hóa đơn chưa PAID (có thể âm: người thuê còn nợ).
 * Điều kiện kiểm theo thứ tự FINAL_READING_UNCONFIRMED → FINAL_INVOICE_MISSING → OPEN_DISPUTES → PENDING_FINDINGS,
 * trả về tất cả điều kiện chưa đạt để chủ nhà thấy một lần.
 *
 * @author Sang
 * @version 1.0
 */
public class SettlementRulesTests
{
    private static SettlementCheck Clear() => new(
        FinalPeriod: "2026-11",
        UnconfirmedReadings: [],
        FinalInvoiceExists: true,
        DisputedFindings: 0,
        DisputedInvoices: 0,
        PendingFindings: 0);

    [Theory]
    [InlineData(7_000_000, 800_000, 4_200_000, 2_000_000)]
    [InlineData(7_000_000, 0, 0, 7_000_000)]
    [InlineData(7_000_000, 800_000, 9_000_000, -2_800_000)]   // người thuê còn nợ
    public void Refund_IsDepositMinusDeductionsMinusUnpaidInvoices(long deposit, long deductions, long unpaid, long expected)
    {
        Assert.Equal(expected, SettlementService.Refund(deposit, deductions, unpaid));
    }

    [Fact]
    public void EverythingDone_NoBlockers()
    {
        Assert.Empty(SettlementService.Blockers(Clear()));
    }

    [Fact]
    public void EveryConditionUnmet_AllListed_InTheAgreedOrder()
    {
        var blockers = SettlementService.Blockers(Clear() with
        {
            UnconfirmedReadings = [MeterKind.Electricity, MeterKind.Water],
            FinalInvoiceExists = false,
            DisputedFindings = 1,
            DisputedInvoices = 2,
            PendingFindings = 3,
        });

        Assert.Equal(
            [ErrorCodes.FinalReadingUnconfirmed, ErrorCodes.FinalInvoiceMissing, ErrorCodes.OpenDisputes, ErrorCodes.PendingFindings],
            blockers.Select(b => b.Code));
        Assert.Equal("Chưa xác nhận chỉ số tháng cuối: điện, nước (kỳ 2026-11)", blockers[0].Message);
        Assert.Equal("Chưa có hóa đơn kỳ 2026-11", blockers[1].Message);
        Assert.Equal("Còn khiếu nại chưa xử lý: 1 mục trừ cọc, 2 hóa đơn", blockers[2].Message);
        Assert.Equal("Người thuê chưa trả lời 3 mục trừ cọc", blockers[3].Message);
    }

    [Fact]
    public void OnlyADisputedInvoice_IsAnOpenDispute()
    {
        var blocker = Assert.Single(SettlementService.Blockers(Clear() with { DisputedInvoices = 1 }));

        Assert.Equal(ErrorCodes.OpenDisputes, blocker.Code);
        Assert.Equal("Còn khiếu nại chưa xử lý: 1 hóa đơn", blocker.Message);
    }

    [Fact]
    public void OnlyWaterUnconfirmed_NamesWater()
    {
        var blocker = Assert.Single(SettlementService.Blockers(Clear() with { UnconfirmedReadings = [MeterKind.Water] }));

        Assert.Equal("Chưa xác nhận chỉ số tháng cuối: nước (kỳ 2026-11)", blocker.Message);
    }
}

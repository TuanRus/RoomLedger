using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Test luật chỉ số và hóa đơn BR-20 đến BR-24.
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1200L, 0.95, 1100L, MeterReadingStatus.PendingConfirm, null)]
    [InlineData(1200L, 0.7, 1100L, MeterReadingStatus.NeedsReview, MeterReadingService.ReasonLowConfidence)]
    [InlineData(1000L, 0.95, 1100L, MeterReadingStatus.NeedsReview, MeterReadingService.ReasonLowerThanPrevious)]
    [InlineData(1500L, 0.95, 1100L, MeterReadingStatus.NeedsReview, MeterReadingService.ReasonUnusualUsage)]
    [InlineData(null, null, 1100L, MeterReadingStatus.NeedsReview, MeterReadingService.ReasonAiFailed)]
    public void Assess_FlagsReadingsThatNeedReview(long? value, double? confidence, long? previous, MeterReadingStatus status, string? reason)
    {
        var result = MeterReadingService.Assess(value, (decimal?)confidence, previous, [100, 110, 90]);
        Assert.Equal(status, result.Status);
        Assert.Equal(reason, result.ReviewReason);
    }

    [Fact]
    public void ValidateConfirmedValue_AllowsLowerValueOnlyWhenMeterReplaced()
    {
        Assert.Throws<ValidationException>(() => MeterReadingService.ValidateConfirmedValue(50, 1100, false));
        MeterReadingService.ValidateConfirmedValue(50, 1100, true);
        Assert.Throws<ValidationException>(() => MeterReadingService.ValidateConfirmedValue(-1, null, true));
    }

    [Fact]
    public void Calculate_UsesUsageTimesRatePlusRentAndFee()
    {
        var invoice = new Invoice();
        InvoiceService.Calculate(invoice, new InvoiceInput(3_500_000, 1_000, 1_120, 3_500, 50, 56, 20_000, 100_000));
        Assert.Equal(420_000, invoice.ElectricityAmount);
        Assert.Equal(120_000, invoice.WaterAmount);
        Assert.Equal(4_140_000, invoice.Total);
    }

    [Fact]
    public void Calculate_OverflowThrowsInsteadOfWrongMoney() =>
        Assert.Throws<OverflowException>(() =>
            InvoiceService.Calculate(new Invoice(), new InvoiceInput(long.MaxValue, 0, 1, 1, 0, 0, 1, 1)));

    [Fact]
    public void EnsurePeriodStarted_RejectsFutureMonthOnly()
    {
        InvoiceService.EnsurePeriodStarted("2026-11", new DateOnly(2026, 11, 30));
        var ex = Assert.Throws<BadRequestException>(() => InvoiceService.EnsurePeriodStarted("2026-12", new DateOnly(2026, 11, 30)));
        Assert.Equal(ErrorCodes.InvalidPeriod, ex.ErrorCode);
    }

    [Fact]
    public void InvoiceLifecycle_DisputedCannotBePaid()
    {
        var invoice = new Invoice();
        InvoiceService.Send(invoice, Now);
        InvoiceService.MarkDisputed(invoice);
        var ex = Assert.Throws<InvalidStateException>(() => InvoiceService.MarkPaid(invoice, Now));
        Assert.Equal("Hãy xử lý khiếu nại trước", ex.Message);
        invoice.Status = InvoiceStatus.Sent;
        InvoiceService.MarkPaid(invoice, Now);
        Assert.Throws<InvalidStateException>(() => InvoiceService.MarkDisputed(invoice));
    }

    [Fact]
    public void ResolveDispute_KeptNeedsResponse()
    {
        var dispute = new Dispute();
        Assert.Throws<ValidationException>(() => DisputeService.Resolve(dispute, DisputeResolution.Kept, " ", Now));
        DisputeService.Resolve(dispute, DisputeResolution.Kept, "Ảnh đồng hồ rõ ràng", Now);
        Assert.Equal(DisputeStatus.Resolved, dispute.Status);
        Assert.Throws<InvalidStateException>(() => DisputeService.Resolve(dispute, DisputeResolution.Kept, "lần hai", Now));
    }
}
